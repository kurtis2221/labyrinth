using System;
using System.IO;
using System.Threading;
using System.Net.Sockets;

namespace labyrinth
{
    class Vector2 { public byte x = 255, y = 255; }

    class Program
    {
        const char PLAYER_CHAR = 'O';
        const char EMPTY_CHAR = ' ';

        static ConsoleColor[] pl_color =
        {
            ConsoleColor.Red,
            ConsoleColor.Green,
            ConsoleColor.DarkCyan,
            ConsoleColor.White,
            ConsoleColor.Yellow,
            ConsoleColor.Cyan,
            ConsoleColor.Gray,
            ConsoleColor.DarkYellow
        };

        static ConsoleColor con_back = ConsoleColor.Black;
        static ConsoleColor con_fore = ConsoleColor.White;

        static TcpListener server;
        static TcpClient client;
        static Socket sock;
        static Thread[] threads;
        static Socket[] players;
        static Thread thd, thd2;
        static Vector2[] pl_pos = new Vector2[8];
        static Vector2 position = new Vector2();
        static byte[] map;
        static bool[,] matrix;
        static byte currpl = 0;
        static bool servstat = false;

        static void Main(string[] args)
        {
            Console.Title = "Labyrinth Multiplayer";
            Console.BackgroundColor = con_back;
            Console.ForegroundColor = con_fore;
            if (!File.Exists("map.txt"))
            {
                Console.WriteLine("map.txt is missing!");
                Console.ReadKey(true);
                return;
            }
            int i;
            map = File.ReadAllBytes("map.txt");
            int rows = 1, cols, c = 0;
            for (i = 0; i < map.Length; i++)
                if (map[i] == 0x0A) rows++;
            for (i = 0; i < map.Length && map[i] != 0x0A; i++) ;
            cols = i;
            matrix = new bool[cols, rows];
            for (i = 0; i < map.Length; i++)
            {
                if (map[i] == 0x0A) continue;
                matrix[c % cols, c / cols] = map[i] == 0x58;
                c++;
            }
            for (i = 0; i < pl_pos.Length; i++)
                pl_pos[i] = new Vector2();
            Console.Write("(C)lient or (S)erver?");
            if (Console.ReadKey(true).Key == ConsoleKey.S)
            {
                try
                {
                    int slots = 0;
                retry:
                    Console.Clear();
                    Console.Write("Max players (2-8):");
                    if (!int.TryParse(Console.ReadLine(), out slots)) goto retry;
                    server = new TcpListener(System.Net.IPAddress.Any, 7777);
                    server.Start();
                    servstat = true;
                    players = new Socket[slots];
                    threads = new Thread[slots];
                    thd = new Thread(new ThreadStart(ServSrc));
                    thd.Start();
                    //
                    DrawMap();
                    client = new TcpClient("127.0.0.1", 7777);
                    sock = client.Client;
                    //
                    thd2 = new Thread(new ThreadStart(ClientRec));
                    thd2.Start();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Source + " - " + e.Message);
                    Console.ReadKey(true);
                    Environment.Exit(0);
                }
            }
            else
            {
                try
                {
                    Console.Clear();
                    Console.Write("IP:");
                    string ip = Console.ReadLine();
                    DrawMap();
                    client = new TcpClient(ip, 7777);
                    sock = client.Client;
                    thd = new Thread(new ThreadStart(ClientRec));
                    thd.Start();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Source + " - " + e.Message);
                    Console.ReadKey(true);
                    Environment.Exit(0);
                }
            }
            Console.CursorVisible = false;
            byte[] buffer = new byte[2];
            position.x = position.y = buffer[0] = buffer[1] = 1;
            ConsoleKey ck;
            while (true)
            {
                ck = Console.ReadKey(true).Key;
                if (ck == ConsoleKey.UpArrow &&
                    !matrix[position.x,position.y-1])
                {
                    position.y -= 1;
                    buffer[1] = position.y;
                    sock.Send(buffer);
                }
                else if (ck == ConsoleKey.DownArrow &&
                    !matrix[position.x, position.y + 1])
                {
                    position.y += 1;
                    buffer[1] = position.y;
                    sock.Send(buffer);
                }
                else if (ck == ConsoleKey.LeftArrow &&
                    !matrix[position.x - 1, position.y])
                {
                    position.x -= 1;
                    buffer[0] = position.x;
                    sock.Send(buffer);
                }
                else if (ck == ConsoleKey.RightArrow &&
                    !matrix[position.x + 1, position.y])
                {
                    position.x += 1;
                    buffer[0] = position.x;
                    sock.Send(buffer);
                }
                else if (ck == ConsoleKey.Escape)
                {
                    thd.Abort();
                    if (thd2 != null)
                        thd2.Abort();
                    Environment.Exit(0);
                }
            }
        }

        //Server thread
        static void ServSrc()
        {
            Socket tmp;
            while (true)
            {
                tmp = server.AcceptSocket();
                for (byte i = 0; i < players.Length; i++)
                {
                    if (players[i] == null)
                    {
                        players[i] = tmp;
                        for (byte i2 = 0; i2 < players.Length; i2++)
                            if (players[i2] != null)
                                players[i2].Send(new byte[] { i, 1, 1 });
                        threads[i] = new Thread(() => ServRec(i));
                        threads[i].Start();
                        currpl++;
                        if (servstat && currpl >= players.Length)
                        {
                            servstat = false;
                            thd.Abort();
                            server.Stop();
                        }
                        break;
                    }
                }
            }
        }

        static void ServRec(byte id)
        {
            try
            {
                byte[] buffer = new byte[2];
                byte[] output = new byte[3];
                while (true)
                {
                    players[id].Receive(buffer);
                    Array.Copy(buffer, 0, output, 1, 2);
                    output[0] = id;
                    for (int i = 0; i < players.Length; i++)
                        if (players[i] != null)
                            players[i].Send(output);
                }
            }
            catch
            {
                players[id].Close();
                players[id] = null;
                threads[id].Abort();
                threads[id] = null;
                for (int i = 0; i < players.Length; i++)
                    if (players[i] != null && i != id)
                        players[i].Send(new byte[] { id, 255, 255 });
                currpl--;
                if (!servstat && currpl < players.Length)
                {
                    servstat = true;
                    thd.Start();
                    server.Start();
                }
                GC.Collect();
            }
        }

        //Client thread
        static void ClientRec()
        {
            try
            {
                int i;
                byte[] buffer = new byte[3];
                while (true)
                {
                    sock.Receive(buffer);
                    if (pl_pos[buffer[0]].x != 255)
                    {
                        Console.ForegroundColor = pl_color[buffer[0]];
                        Console.SetCursorPosition(pl_pos[buffer[0]].x, pl_pos[buffer[0]].y);
                        Console.Write(EMPTY_CHAR);
                    }
                    pl_pos[buffer[0]].x = buffer[1];
                    pl_pos[buffer[0]].y = buffer[2];
                    for (i = 0; i < pl_pos.Length; i++)
                    {
                        if (pl_pos[i].x != 255)
                        {
                            Console.ForegroundColor = pl_color[i];
                            Console.SetCursorPosition(pl_pos[i].x, pl_pos[i].y);
                            Console.Write(PLAYER_CHAR);
                        }
                    }
                }
            }
            catch
            {
                sock.Close();
                Console.Clear();
                Console.ForegroundColor = con_fore;
                Console.WriteLine("Disconnected.");
                Console.Read();
            }
        }

        static void DrawMap()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Blue;
            for (int i = 0; i < map.Length; i++)
                Console.Write((char)map[i]);
            Console.ForegroundColor = ConsoleColor.Green;
        }
    }
}