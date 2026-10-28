using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace HalloweenMaze
{
    internal static class Program
    {
        // ============ CONFIGURAÇÃO (ajusta aqui a dificuldade) ============
        const string SecretCode = "UVQ";                       // exatamente 3 caracteres!!!!!!!!!!!!!!
        const int CellsW = 20;                                 // largura do labirinto (em células)
        const int CellsH = 10;                                  // altura do labirinto (em células)
        const int ExtraLoops = 18;                             // paredes removidas para criar atalhos (mais = mais fácil fugir)
        const int PlayerMoveMs = 110;                          // intervalo mínimo entre passos do jogador
        static readonly int[] MonsterMoveMs = { 200, 180, 140 }; // intervalo entre passos do monstro (0, 1, 2 letras apanhadas)
        const int GraceMs = 2000;                              // tempo até o monstro acordar (início e após captura)
        const int CaughtPauseMs = 1500;                        // pausa depois de seres apanhado
        // ==================================================================

        const int Cols = 2 * CellsW + 1;
        const int Rows = 2 * CellsH + 1;
        const int MazeTop = 3;                                 // linha do ecrã onde começa o labirinto
        const int StatusRow = MazeTop + Rows + 1;

        static readonly Random Rng = new Random();
        static readonly bool[,] Wall = new bool[Cols, Rows];   // true = parede
        static readonly (int x, int y)[] Letters = new (int x, int y)[3];
        static readonly bool[] Got = new bool[3];
        static readonly (int dx, int dy)[] Dirs4 = { (0, -1), (1, 0), (0, 1), (-1, 0) };

        static int px, py;   // jogador
        static int mx, my;   // monstro

        static void Main()
        {

           
            if (SecretCode.Length != 3)
                throw new InvalidOperationException("SecretCode tem de ter exatamente 3 caracteres.");

            Console.OutputEncoding = Encoding.UTF8;
            try { Console.CursorVisible = false; } catch { /* consola sem suporte */ }

            ShowIntro();
            Console.CursorVisible = false;
            Console.Title = "LOAD - Carregando...";

            MostrarBanner();
            CarregarBarraDeProgresso();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n\n      Carregamento concluído com sucesso!");
            Console.ResetColor();

            Console.CursorVisible = true;
        }

        static void MostrarBanner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"
 ___       ________  ________  ________  ___  ________   ________     
|\  \     |\   __  \|\   __  \|\   ___ \|\  \|\   ___  \|\   ____\    
\ \  \    \ \  \|\  \ \  \|\  \ \  \_|\ \ \  \ \  \\ \  \ \  \___|    
 \ \  \    \ \  \\\  \ \   __  \ \  \ \\ \ \  \ \  \\ \  \ \  \  ___  
  \ \  \____\ \  \\\  \ \  \ \  \ \  \_\\ \ \  \ \  \\ \  \ \  \|\  \ 
   \ \_______\ \_______\ \__\ \__\ \_______\ \__\ \__\\ \__\ \_______\
    \|_______|\|_______|\|__|\|__|\|_______|\|__|\|__| \|__|\|_______|
");
            Console.ResetColor();
        }

        static void CarregarBarraDeProgresso()
        {
            // Cada posição representa um "degrau" da barra (0 a 10)
            string[] barras =
            {
                "▒▒▒▒▒▒▒▒▒▒",
                "█▒▒▒▒▒▒▒▒▒",
                "██▒▒▒▒▒▒▒▒",
                "███▒▒▒▒▒▒▒",
                "████▒▒▒▒▒▒",
                "█████▒▒▒▒▒",
                "██████▒▒▒▒",
                "███████▒▒▒",
                "████████▒▒",
                "█████████▒",
                "██████████"
            };

            Console.WriteLine();

            for (int i = 0; i < barras.Length; i++)
            {
                int percentual = i * 10;

                // \r retorna o cursor para o início da linha, sobrescrevendo o conteúdo
                Console.Write($"\r      Carregando [{barras[i]}] {percentual,3}%");

                Thread.Sleep(300); // Simula o tempo de carregamento
            }

            GenerateMaze();
            PlaceLetters();

            Console.Clear();
            Play();

            ShowEnding();
            try { Console.CursorVisible = true; } catch { }
        }

        // ============================== JOGO ==============================
        static void Play()
        {
            int collected = 0;
            int captures = 0;
            bool monsterAwake = false;

            px = 1; py = 1;
            mx = Cols - 2; my = Rows - 2;

            DrawFrame();
            DrawAll();
            DrawHud(captures);
            WriteStatus("Corre! Ele acorda em instantes...", ConsoleColor.Gray);
            FlushKeys();

            var sw = Stopwatch.StartNew();
            long lastMove = 0;
            long nextMonster = GraceMs;

            while (collected < 3)
            {
                long now = sw.ElapsedMilliseconds;

                // --- input ---
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);   // teclas durante o cooldown são descartadas
                    if (now - lastMove >= PlayerMoveMs)
                    {
                        var (dx, dy) = DirFor(key.Key);
                        if ((dx != 0 || dy != 0) && TryMovePlayer(dx, dy))
                        {
                            lastMove = now;
                            int li = LetterAt(px, py);
                            if (li >= 0 && !Got[li])
                            {
                                Got[li] = true;
                                collected++;
                                DrawHud(captures);
                                WriteStatus("Letra " + (li + 1) + " recolhida!" +
                                            (collected < 3 ? " O monstro ficou mais rápido." : ""),
                                            ConsoleColor.Yellow);
                            }
                        }
                    }
                }

                // --- monstro ---
                if (now >= nextMonster)
                {
                    MoveMonster();
                    nextMonster = now + MonsterMoveMs[Math.Min(collected, 2)];
                    if (!monsterAwake)
                    {
                        monsterAwake = true;
                        WriteStatus("Ele acordou! Não pares.", ConsoleColor.Red);
                    }
                }

                // --- captura ---
                if (collected < 3 && px == mx && py == my)
                {
                    captures++;
                    WriteStatus("O MONSTRO APANHOU-TE! De volta ao início...", ConsoleColor.Red);
                    Thread.Sleep(CaughtPauseMs);

                    int ox = px, oy = py, omx = mx, omy = my;
                    px = 1; py = 1;
                    mx = Cols - 2; my = Rows - 2;
                    DrawTile(ox, oy); DrawTile(omx, omy);
                    DrawTile(px, py); DrawTile(mx, my);

                    DrawHud(captures);
                    WriteStatus("Corre! Ele acorda em instantes...", ConsoleColor.Gray);
                    FlushKeys();
                    monsterAwake = false;
                    now = sw.ElapsedMilliseconds;
                    lastMove = now;
                    nextMonster = now + GraceMs;
                }

                Thread.Sleep(1);
            }
        }

        static bool TryMovePlayer(int dx, int dy)
        {
            int nx = px + dx, ny = py + dy;
            if (nx < 0 || ny < 0 || nx >= Cols || ny >= Rows || Wall[nx, ny]) return false;

            int ox = px, oy = py;
            px = nx; py = ny;
            DrawTile(ox, oy);
            DrawTile(px, py);
            return true;
        }

        // O monstro dá um passo na direção do jogador pelo caminho mais curto.
        static void MoveMonster()
        {
            var dist = Bfs(px, py);
            int best = dist[mx, my];
            int bx = mx, by = my;

            foreach (var (dx, dy) in Dirs4)
            {
                int nx = mx + dx, ny = my + dy;
                if (nx < 0 || ny < 0 || nx >= Cols || ny >= Rows || Wall[nx, ny]) continue;
                if (dist[nx, ny] >= 0 && dist[nx, ny] < best)
                {
                    best = dist[nx, ny];
                    bx = nx; by = ny;
                }
            }

            if (bx == mx && by == my) return;

            int ox = mx, oy = my;
            mx = bx; my = by;
            DrawTile(ox, oy);
            DrawTile(mx, my);
        }

        static (int, int) DirFor(ConsoleKey k)
        {
            switch (k)
            {
                case ConsoleKey.UpArrow:
                case ConsoleKey.W: return (0, -1);
                case ConsoleKey.RightArrow:
                case ConsoleKey.D: return (1, 0);
                case ConsoleKey.DownArrow:
                case ConsoleKey.S: return (0, 1);
                case ConsoleKey.LeftArrow:
                case ConsoleKey.A: return (-1, 0);
                default: return (0, 0);
            }
        }

        static int LetterAt(int x, int y)
        {
            for (int i = 0; i < 3; i++)
                if (Letters[i].x == x && Letters[i].y == y) return i;
            return -1;
        }

        static void FlushKeys()
        {
            while (Console.KeyAvailable) Console.ReadKey(true);
        }

        // ============================ LABIRINTO ============================
        static void GenerateMaze()
        {
            for (int x = 0; x < Cols; x++)
                for (int y = 0; y < Rows; y++)
                    Wall[x, y] = true;

            // Recursive backtracker (iterativo): garante que todas as células ficam ligadas.
            var stack = new Stack<(int x, int y)>();
            Wall[1, 1] = false;
            stack.Push((1, 1));

            while (stack.Count > 0)
            {
                var (cx, cy) = stack.Peek();
                var options = new List<(int dx, int dy)>();

                foreach (var (dx, dy) in Dirs4)
                {
                    int nx = cx + dx * 2, ny = cy + dy * 2;
                    if (nx > 0 && ny > 0 && nx < Cols - 1 && ny < Rows - 1 && Wall[nx, ny])
                        options.Add((dx, dy));
                }

                if (options.Count == 0) { stack.Pop(); continue; }

                var (ddx, ddy) = options[Rng.Next(options.Count)];
                Wall[cx + ddx, cy + ddy] = false;
                Wall[cx + ddx * 2, cy + ddy * 2] = false;
                stack.Push((cx + ddx * 2, cy + ddy * 2));
            }

            // Cria ciclos: sem eles o labirinto é uma árvore e o monstro encurrala o jogador num beco.
            int removed = 0, guard = 0;
            while (removed < ExtraLoops && guard++ < 5000)
            {
                int x = Rng.Next(1, Cols - 1);
                int y = Rng.Next(1, Rows - 1);
                if (!Wall[x, y]) continue;

                bool vertical = (x % 2 == 1 && y % 2 == 0) && !Wall[x, y - 1] && !Wall[x, y + 1];
                bool horizontal = (x % 2 == 0 && y % 2 == 1) && !Wall[x - 1, y] && !Wall[x + 1, y];

                if (vertical || horizontal)
                {
                    Wall[x, y] = false;
                    removed++;
                }
            }
        }

        static int[,] Bfs(int sx, int sy)
        {
            var dist = new int[Cols, Rows];
            for (int x = 0; x < Cols; x++)
                for (int y = 0; y < Rows; y++)
                    dist[x, y] = -1;

            var q = new Queue<(int x, int y)>();
            dist[sx, sy] = 0;
            q.Enqueue((sx, sy));

            while (q.Count > 0)
            {
                var (cx, cy) = q.Dequeue();
                foreach (var (dx, dy) in Dirs4)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= Cols || ny >= Rows) continue;
                    if (Wall[nx, ny] || dist[nx, ny] >= 0) continue;
                    dist[nx, ny] = dist[cx, cy] + 1;
                    q.Enqueue((nx, ny));
                }
            }
            return dist;
        }

        // Espalha as 3 letras longe do início, do monstro e umas das outras.
        static void PlaceLetters()
        {
            var fromStart = Bfs(1, 1);
            var fromMonster = Bfs(Cols - 2, Rows - 2);

            var candidates = new List<(int x, int y)>();
            for (int y = 1; y < Rows; y += 2)
                for (int x = 1; x < Cols; x += 2)
                    if (!Wall[x, y]) candidates.Add((x, y));

            candidates = candidates.OrderBy(_ => Rng.Next()).ToList();

            foreach (int minDist in new[] { 14, 10, 7, 4, 0 })
            {
                var picked = new List<(int x, int y)>();

                foreach (var c in candidates)
                {
                    if ((c.x == 1 && c.y == 1) || (c.x == Cols - 2 && c.y == Rows - 2)) continue;
                    if (fromStart[c.x, c.y] < minDist || fromMonster[c.x, c.y] < minDist / 2) continue;
                    if (!picked.All(p => Math.Abs(p.x - c.x) + Math.Abs(p.y - c.y) >= minDist)) continue;

                    picked.Add(c);
                    if (picked.Count == 3) break;
                }

                if (picked.Count == 3)
                {
                    for (int i = 0; i < 3; i++) Letters[i] = picked[i];
                    return;
                }
            }
        }

        // ============================ INTERFACE ============================
        static void ShowIntro()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("==============================================");
            Console.WriteLine("            LABIRINTO AMALDIÇOADO");
            Console.WriteLine("==============================================");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine(" A porta só abre com um código de 3 caracteres.");
            Console.WriteLine(" Cada caracter está escondido num '??' dentro do");
            Console.WriteLine(" labirinto. Apanha os 3.");
            Console.WriteLine();
            Console.WriteLine(" Tu és o '@@'. Move-te com as SETAS ou WASD.");
            Console.WriteLine(" O 'MM' acorda passado uns segundos e persegue-te");
            Console.WriteLine(" sem parar, e acelera sempre que apanhas uma letra.");
            Console.WriteLine();
            Console.WriteLine(" Se ele te apanhar, voltas ao início, mas manténs");
            Console.WriteLine(" as letras que já recolheste.");
            Console.WriteLine();

            int needW = 2 * Cols + 2, needH = StatusRow + 2;
            try
            {
                if (Console.WindowWidth < needW || Console.WindowHeight < needH)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(" AVISO: aumenta a janela da consola para pelo menos");
                    Console.WriteLine(" " + needW + " colunas x " + needH + " linhas, senão o jogo parte.");
                    Console.ResetColor();
                    Console.WriteLine();
                }
            }
            catch { /* ignora */ }

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(" [ESPAÇO] para começar");
            Console.ResetColor();

            while (true)
            {
                if (Console.ReadKey(true).Key == ConsoleKey.Spacebar) break;
            }
        }

        static void DrawFrame()
        {
            Console.SetCursorPosition(0, 0);
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write(" === LABIRINTO AMALDIÇOADO ===");
            Console.ResetColor();

            Console.SetCursorPosition(1, StatusRow + 1);
            Console.ForegroundColor = ConsoleColor.Cyan; Console.Write("@@");
            Console.ResetColor(); Console.Write(" tu   ");
            Console.ForegroundColor = ConsoleColor.Red; Console.Write("MM");
            Console.ResetColor(); Console.Write(" monstro   ");
            Console.ForegroundColor = ConsoleColor.Green; Console.Write("??");
            Console.ResetColor(); Console.Write(" letra   [setas / WASD]");
        }

        static void DrawAll()
        {
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Cols; x++)
                    DrawTile(x, y);
        }

        // Cada célula ocupa 2 caracteres para o labirinto não ficar esticado na vertical.
        static void DrawTile(int x, int y)
        {
            Console.SetCursorPosition(1 + x * 2, MazeTop + y);

            if (x == mx && y == my)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("MM");
            }
            else if (x == px && y == py)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("@@");
            }
            else
            {
                int li = LetterAt(x, y);
                if (li >= 0 && !Got[li])
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("??");
                }
                else if (Wall[x, y])
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("##");
                }
                else
                {
                    Console.Write("  ");
                }
            }
            Console.ResetColor();
        }

        static void DrawHud(int captures)
        {
            Console.SetCursorPosition(0, 1);
            Console.Write(" CÓDIGO: [ ");
            for (int i = 0; i < 3; i++)
            {
                if (Got[i])
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write(SecretCode[i] + " ");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("_ ");
                }
            }
            Console.ResetColor();
            Console.Write("]    Capturas: " + captures + "     ");
        }

        static void WriteStatus(string message, ConsoleColor color)
        {
            Console.SetCursorPosition(1, StatusRow);
            Console.ForegroundColor = color;
            Console.Write(message.PadRight(2 * Cols));
            Console.ResetColor();
        }

        static void ShowEnding()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("==============================================");
            Console.WriteLine("            ESCAPASTE DO LABIRINTO");
            Console.WriteLine("==============================================");
            Console.ResetColor();
            Console.WriteLine();
            Console.Write(" O código é:  ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(SecretCode);
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine(" Guarde este código!");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(" [qualquer tecla para sair]");
            Console.ResetColor();
            Console.ReadKey(true);
        }
    }
}