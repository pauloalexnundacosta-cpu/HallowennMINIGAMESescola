using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace MonsterEscape
{
    internal static class Program
    {
        // ============ CONFIGURAÇÃO (ajusta aqui a dificuldade) ============
        const string SecretCode = "7K3";   // código mostrado no ecrã de vitória (exatamente 3 caracteres)

        const int PlayerMoveMs = 110;      // intervalo entre passos do jogador

        // Monstro: o intervalo começa em MonsterBaseMs (MENOR que o do jogador = mais rápido)
        // e vai diminuindo MonsterRampPerSec milissegundos por cada segundo desde que acordou,
        // até ao mínimo MonsterMinMs.
        const int MonsterBaseMs = 90;
        const int MonsterMinMs = 40;
        const double MonsterRampPerSec = 0.5;

        const int WakeGraceMs = 2500;      // tempo até o monstro começar a mexer depois das luzes voltarem
        const int CaughtPauseMs = 2000;    // pausa depois de seres apanhado

        // Campo de visão
        const int SightCircleDark = 2;     // raio de visão sem lanterna
        const int SightCircleLamp = 2;     // raio à volta do jogador com lanterna
        const int SightCircleLit = 3;      // raio com as luzes ligadas
        const double ConeRange = 9;        // alcance do cone da lanterna
        const double ConeRangeLit = 13;    // alcance do cone com as luzes ligadas
        const double ConeHalfAngleDeg = 45;// meio-ângulo do cone
        const bool RememberMap = false;    // true = as paredes/salas já vistas ficam desenhadas (sem itens nem monstro)
        // ==================================================================

        const int Cols = 47;
        const int Rows = 21;
        const int MazeTop = 4;
        const int StatusRow = MazeTop + Rows + 1;

        static readonly double ConeCos = Math.Cos(ConeHalfAngleDeg * Math.PI / 180.0);

        static readonly Random Rng = new Random();
        static readonly char[,] Map = new char[Cols, Rows];   // '#' parede, ' ' chão, 'L' armário, 'D' porta do monstro, 'E' porta da saída
        static readonly Dictionary<(int, int), string> Glyph = new Dictionary<(int, int), string>();
        static readonly (int dx, int dy)[] Dirs4 = { (0, -1), (1, 0), (0, 1), (-1, 0) };

        static bool[,] Vis = new bool[Cols, Rows];            // o que o jogador vê agora
        static readonly bool[,] Seen = new bool[Cols, Rows];  // o que já viu alguma vez

        // Salas: nome (HUD), etiqueta (mapa), x1, y1, x2, y2
        // O mapa é uma árvore: só existe UM caminho entre quaisquer duas salas.
        static readonly (string name, string label, int x1, int y1, int x2, int y2)[] Rooms =
        {
            ("Casa de banho",   "BANHEIRO",        2, 10,  9, 14),
            ("Refeitório",      "REFEITORIO",     17,  8, 27, 14),
            ("Mad Bay",         "MAD BAY",        17,  2, 27,  5),
            ("Armazém",         "ARMAZEM",         2,  2,  9,  6),
            ("Comunicações",    "COMUNICACOES",   17, 16, 27, 19),
            ("Sala das luzes",  "LUZES",           2, 17,  9, 19),
            ("Sala do monstro", "SALA DO MONSTRO",36, 16, 44, 19),
            ("Saída",           "SAIDA",          37,  8, 44, 12),
        };
        const int CommsIdx = 4;

        // Posições importantes
        static readonly (int x, int y) StartPos = (5, 12);
        static readonly (int x, int y) RespawnPos = (4, 4);       // sala do fusível (armazém)
        static readonly (int x, int y) LampPos = (25, 13);
        static readonly (int x, int y) FusePos = (8, 5);
        static readonly (int x, int y) FuseBoxPos = (3, 19);
        static readonly (int x, int y) MedalPos = (43, 18);
        static readonly (int x, int y) MonsterHome = (40, 18);
        static readonly (int x, int y) DoorMonster = (35, 18);
        static readonly (int x, int y) DoorExit = (36, 10);

        // Estado
        static int px, py, mx, my, lastMx, lastMy;
        static int facX = 1, facY = 0;
        static bool hasLamp, hasFuse, lightsOn, hasMedal, hasKey;
        static bool monsterAwake, monsterAlive = true, escaped, commsHide;
        static int captures;
        static Stopwatch sw;
        static long nextMonster, wakeStart;

        static void Main()
        {
            if (SecretCode.Length != 3)
                throw new InvalidOperationException("SecretCode tem de ter exatamente 3 caracteres.");

            Console.OutputEncoding = Encoding.UTF8;
            try { Console.CursorVisible = false; } catch { }

            ShowIntro();
            BuildMap();

            Console.Clear();
            Play();

            ShowEnding();
            try { Console.CursorVisible = true; } catch { }
        }

        // ============================== JOGO ==============================
        static void Play()
        {
            px = StartPos.x; py = StartPos.y;
            mx = MonsterHome.x; my = MonsterHome.y;
            lastMx = mx; lastMy = my;

            DrawFrame();
            ResetVision();
            DrawAll();
            DrawHud();
            WriteStatus("Acordas na casa de banho. Está tudo às escuras e em silêncio...", ConsoleColor.Gray);
            FlushKeys();

            sw = Stopwatch.StartNew();
            long lastMove = 0;

            while (!escaped)
            {
                long now = sw.ElapsedMilliseconds;

                // --- input ---
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);   // teclas durante o cooldown são descartadas
                    if (now - lastMove >= PlayerMoveMs)
                    {
                        var (dx, dy) = DirFor(key.Key);
                        if (dx != 0 || dy != 0)
                        {
                            bool turned = facX != dx || facY != dy;
                            facX = dx; facY = dy;

                            if (TryMovePlayer(dx, dy))
                            {
                                lastMove = now;
                                OnPlayerMoved(now);
                            }
                            else if (turned && !escaped)
                            {
                                UpdateVision();   // virar-se também muda o campo de visão
                            }
                            if (escaped) break;
                        }
                    }
                }

                // --- monstro ---
                if (monsterAlive && monsterAwake && now >= nextMonster)
                {
                    MoveMonster();
                    nextMonster = now + CurrentMonsterMs(now);
                }

                // --- contacto com o monstro ---
                if (monsterAlive && monsterAwake && px == mx && py == my)
                {
                    if (hasMedal)
                        KillMonster();
                    else
                        Captured();
                    now = sw.ElapsedMilliseconds;
                    lastMove = now;
                }

                Thread.Sleep(1);
            }
        }

        // Quanto mais tempo o monstro está acordado, mais rápido fica.
        static int CurrentMonsterMs(long now)
        {
            double t = Math.Max(0, now - wakeStart) / 1000.0;
            return (int)Math.Max(MonsterMinMs, MonsterBaseMs - t * MonsterRampPerSec);
        }

        static bool TryMovePlayer(int dx, int dy)
        {
            int nx = px + dx, ny = py + dy;
            if (nx < 0 || ny < 0 || nx >= Cols || ny >= Rows) return false;

            char t = Map[nx, ny];
            if (t == '#') return false;

            if (t == 'D')
            {
                WriteStatus("A porta da sala do monstro não abre. Sem luz, não vais entrar ali.", ConsoleColor.Red);
                return false;
            }

            if (t == 'E')
            {
                if (hasKey) { escaped = true; return false; }
                WriteStatus("A porta da saída está trancada. A chave está no pescoço do monstro.", ConsoleColor.Red);
                return false;
            }

            // Sem lanterna não se encontram armários, fusível nem medalhão.
            if (!hasLamp && (t == 'L' || IsFuseVisible(nx, ny) || IsMedalVisible(nx, ny)))
            {
                WriteStatus("Está muito escuro aqui... não consigo ver nada. Preciso de uma luz.", ConsoleColor.DarkYellow);
                return false;
            }

            int ox = px, oy = py;
            px = nx; py = ny;
            DrawTile(ox, oy);
            DrawTile(px, py);
            return true;
        }

        static void OnPlayerMoved(long now)
        {
            // Sala de comunicações: o monstro desaparece do "radar".
            bool inComms = InComms();
            if (inComms != commsHide)
            {
                commsHide = inComms;
                if (monsterAlive) DrawTile(mx, my);
                if (inComms) WriteStatus("Sala de comunicações: o sinal está interferido, não vês onde o monstro está.", ConsoleColor.DarkCyan);
            }

            if (px == LampPos.x && py == LampPos.y && !hasLamp)
            {
                hasLamp = true;
                DrawAll();
                WriteStatus("Apanhaste a lanterna! Agora vês em frente e consegues ver armários e objetos.", ConsoleColor.Yellow);
            }
            else if (IsFuseVisible(px, py) && hasLamp)
            {
                hasFuse = true;
                WriteStatus("Encontraste o fusível! Leva-o para a sala das luzes.", ConsoleColor.Magenta);
            }
            else if (IsMedalVisible(px, py) && hasLamp)
            {
                hasMedal = true;
                WriteStatus("Medalhão da cruz! Toca no monstro com ele.", ConsoleColor.White);
            }
            else if (px == FuseBoxPos.x && py == FuseBoxPos.y)
            {
                if (hasFuse)
                {
                    hasFuse = false;
                    lightsOn = true;
                    monsterAwake = true;
                    Map[DoorMonster.x, DoorMonster.y] = ' ';
                    DrawAll();
                    wakeStart = now + WakeGraceMs;
                    nextMonster = wakeStart;
                    WriteStatus("A LUZ VOLTOU! Ouves a porta do monstro a abrir... ele acordou!", ConsoleColor.Red);
                }
                else if (!lightsOn)
                {
                    WriteStatus("A caixa de luzes está sem fusível.", ConsoleColor.Gray);
                }
            }
            else if (Map[px, py] == 'L' && hasLamp)
            {
                WriteStatus(monsterAwake
                    ? "Estás escondido no armário. O monstro perdeu-te de vista e vagueia ao acaso."
                    : "Estás dentro do armário.", ConsoleColor.Green);
            }

            UpdateVision();
            DrawHud();
        }

        static void KillMonster()
        {
            monsterAlive = false;
            hasKey = true;
            int ox = mx, oy = my;
            mx = -1; my = -1;
            DrawTile(ox, oy);
            DrawTile(px, py);
            DrawTile(DoorExit.x, DoorExit.y);
            DrawHud();
            WriteStatus("O medalhão queimou o monstro! Ficaste com a chave. Corre para a saída!", ConsoleColor.Green);
        }

        static void Captured()
        {
            captures++;
            WriteStatus("O MONSTRO APANHOU-TE!", ConsoleColor.Red);
            Thread.Sleep(CaughtPauseMs);

            // Volta para o armazém, as luzes apagam-se e o monstro adormece outra vez.
            px = RespawnPos.x; py = RespawnPos.y;
            mx = MonsterHome.x; my = MonsterHome.y;
            lastMx = mx; lastMy = my;
            facX = 1; facY = 0;
            lightsOn = false;
            hasFuse = false;
            monsterAwake = false;
            commsHide = false;
            Map[DoorMonster.x, DoorMonster.y] = 'D';

            ResetVision();
            DrawAll();
            DrawHud();
            WriteStatus("Acordas no armazém. As luzes apagaram-se. Encontra o fusível de novo!", ConsoleColor.Gray);
            FlushKeys();
        }

        // O monstro persegue pelo caminho mais curto, ou vagueia se o jogador estiver escondido.
        static void MoveMonster()
        {
            int bx = mx, by = my;
            bool hidden = Map[px, py] == 'L';
            bool moved = false;

            if (!hidden)
            {
                var dist = Bfs(px, py);
                int best = dist[mx, my];
                if (best >= 0)
                {
                    foreach (var (dx, dy) in Dirs4)
                    {
                        int nx = mx + dx, ny = my + dy;
                        if (!CanMonsterWalk(nx, ny)) continue;
                        if (dist[nx, ny] >= 0 && dist[nx, ny] < best)
                        {
                            best = dist[nx, ny];
                            bx = nx; by = ny;
                            moved = true;
                        }
                    }
                }
            }

            if (hidden || !moved)
            {
                var options = new List<(int x, int y)>();
                foreach (var (dx, dy) in Dirs4)
                {
                    int nx = mx + dx, ny = my + dy;
                    if (CanMonsterWalk(nx, ny)) options.Add((nx, ny));
                }
                if (options.Count == 0) return;

                // evita voltar para trás, a não ser que seja um beco
                if (options.Count > 1) options.RemoveAll(o => o.x == lastMx && o.y == lastMy);
                var pick = options[Rng.Next(options.Count)];
                bx = pick.x; by = pick.y;
            }

            if (bx == mx && by == my) return;

            int ox = mx, oy = my;
            lastMx = ox; lastMy = oy;
            mx = bx; my = by;
            DrawTile(ox, oy);
            DrawTile(mx, my);
        }

        static bool CanMonsterWalk(int x, int y)
        {
            return x >= 0 && y >= 0 && x < Cols && y < Rows && Map[x, y] == ' ';
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

        static bool IsFuseVisible(int x, int y)
        {
            return !hasFuse && !lightsOn && x == FusePos.x && y == FusePos.y;
        }

        static bool IsMedalVisible(int x, int y)
        {
            return !hasMedal && x == MedalPos.x && y == MedalPos.y;
        }

        static int RoomAt(int x, int y)
        {
            for (int i = 0; i < Rooms.Length; i++)
            {
                var r = Rooms[i];
                if (x >= r.x1 && x <= r.x2 && y >= r.y1 && y <= r.y2) return i;
            }
            return -1;
        }

        static bool InComms()
        {
            return RoomAt(px, py) == CommsIdx;
        }

        static void FlushKeys()
        {
            while (Console.KeyAvailable) Console.ReadKey(true);
        }

        // ========================= CAMPO DE VISÃO =========================
        static bool IsOpaque(int x, int y)
        {
            char t = Map[x, y];
            return t == '#' || t == 'D' || t == 'E';
        }

        // Linha de visão (Bresenham): paredes e portas fechadas bloqueiam.
        static bool HasLos(int x0, int y0, int x1, int y1)
        {
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            int x = x0, y = y0;

            while (!(x == x1 && y == y1))
            {
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx) { err += dx; y += sy; }
                if (x == x1 && y == y1) break;
                if (IsOpaque(x, y)) return false;
            }
            return true;
        }

        static void ComputeVision(bool[,] v)
        {
            Array.Clear(v, 0, v.Length);

            int circle = lightsOn ? SightCircleLit : (hasLamp ? SightCircleLamp : SightCircleDark);
            double range = !hasLamp ? 0 : (lightsOn ? ConeRangeLit : ConeRange);
            int reach = (int)Math.Ceiling(Math.Max(circle, range));

            for (int dy = -reach; dy <= reach; dy++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = px + dx, y = py + dy;
                    if (x < 0 || y < 0 || x >= Cols || y >= Rows) continue;

                    double d2 = dx * dx + dy * dy;
                    bool inCircle = d2 <= circle * circle + 0.5;
                    bool inCone = false;

                    if (!inCircle && range > 0)
                    {
                        double dist = Math.Sqrt(d2);
                        if (dist <= range)
                        {
                            double dot = (dx * facX + dy * facY) / dist;
                            inCone = dot >= ConeCos;
                        }
                    }

                    if ((inCircle || inCone) && HasLos(px, py, x, y))
                        v[x, y] = true;
                }
            }
            v[px, py] = true;
        }

        // Recalcula tudo (sem desenhar). Usado no início e depois de seres apanhado.
        static void ResetVision()
        {
            var nv = new bool[Cols, Rows];
            ComputeVision(nv);
            Vis = nv;
            for (int x = 0; x < Cols; x++)
                for (int y = 0; y < Rows; y++)
                    if (Vis[x, y]) Seen[x, y] = true;
        }

        // Recalcula e redesenha só os tiles cuja visibilidade mudou.
        static void UpdateVision()
        {
            var nv = new bool[Cols, Rows];
            ComputeVision(nv);

            for (int x = 0; x < Cols; x++)
            {
                for (int y = 0; y < Rows; y++)
                {
                    if (nv[x, y] != Vis[x, y])
                    {
                        Vis[x, y] = nv[x, y];
                        if (nv[x, y]) Seen[x, y] = true;
                        DrawTile(x, y);
                    }
                }
            }
        }

        // ============================== MAPA ==============================
        static void Carve(int x1, int y1, int x2, int y2)
        {
            for (int x = x1; x <= x2; x++)
                for (int y = y1; y <= y2; y++)
                    Map[x, y] = ' ';
        }

        static void BuildMap()
        {
            for (int x = 0; x < Cols; x++)
                for (int y = 0; y < Rows; y++)
                    Map[x, y] = '#';

            foreach (var r in Rooms)
            {
                Carve(r.x1, r.y1, r.x2, r.y2);

                string t = r.label;
                if (t.Length % 2 == 1) t += " ";
                for (int i = 0; i < t.Length / 2; i++)
                    Glyph[(r.x1 + i, r.y1)] = t.Substring(i * 2, 2);
            }

            // Corredores estreitos (1 de largura). 7 ligações para 8 salas = só um caminho para cada sala.
            Carve(5, 7, 5, 9);       // armazém        <-> casa de banho
            Carve(5, 15, 5, 16);     // casa de banho  <-> sala das luzes
            Carve(10, 12, 16, 12);   // casa de banho  <-> refeitório
            Carve(22, 6, 22, 7);     // refeitório     <-> Mad Bay
            Carve(22, 15, 22, 15);   // refeitório     <-> comunicações
            Carve(28, 10, 35, 10);   // refeitório     <-> saída
            Carve(28, 18, 34, 18);   // comunicações   <-> sala do monstro

            Map[DoorExit.x, DoorExit.y] = 'E';
            Map[DoorMonster.x, DoorMonster.y] = 'D';

            // Armários (Mad Bay e sala das luzes)
            Map[26, 5] = 'L';
            Map[9, 19] = 'L';
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
                    if (!CanMonsterWalk(nx, ny) || dist[nx, ny] >= 0) continue;
                    dist[nx, ny] = dist[cx, cy] + 1;
                    q.Enqueue((nx, ny));
                }
            }
            return dist;
        }

        // ============================ INTERFACE ============================
        static void ShowIntro()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("==============================================");
            Console.WriteLine("            NAVE AMALDIÇOADA");
            Console.WriteLine("==============================================");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine(" Acordas na casa de banho de uma nave às escuras.");
            Console.WriteLine(" Um monstro dorme na sala dele, com a chave da");
            Console.WriteLine(" saída pendurada ao pescoço.");
            Console.WriteLine();
            Console.WriteLine(" 1. Apanha a lanterna no refeitório.");
            Console.WriteLine(" 2. Encontra o fusível e leva-o à sala das luzes.");
            Console.WriteLine("    (as luzes voltam, mas o monstro ACORDA)");
            Console.WriteLine(" 3. Apanha o medalhão da cruz na sala do monstro e");
            Console.WriteLine("    toca-lhe com ele para ficares com a chave.");
            Console.WriteLine(" 4. Vai à sala da saída, abre a porta e foge.");
            Console.WriteLine();
            Console.WriteLine(" - Só vês o que está no teu campo de visão. A lanterna");
            Console.WriteLine("   ilumina em frente: vira-te para onde queres olhar.");
            Console.WriteLine(" - Sem lanterna não encontras armários, fusível nem medalhão.");
            Console.WriteLine(" - Os armários (Mad Bay e luzes) escondem-te do monstro.");
            Console.WriteLine(" - Nas comunicações não vês onde o monstro está.");
            Console.WriteLine(" - O monstro é mais rápido que tu e ainda acelera com o tempo.");
            Console.WriteLine(" - Se ele te apanhar, acordas no armazém, as luzes");
            Console.WriteLine("   apagam-se e tens de refazer o fusível.");
            Console.WriteLine();
            Console.WriteLine(" Move-te com as SETAS ou WASD.");
            Console.WriteLine();

            int needW = 2 * Cols + 2, needH = StatusRow + 3;
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
            catch { }

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
            Console.Write(" === NAVE AMALDIÇOADA ===");
            Console.ResetColor();

            Console.SetCursorPosition(1, StatusRow + 1);
            Console.ForegroundColor = ConsoleColor.Cyan; Console.Write("@@");
            Console.ResetColor(); Console.Write(" tu  ");
            Console.ForegroundColor = ConsoleColor.Red; Console.Write("MM");
            Console.ResetColor(); Console.Write(" monstro  ");
            Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("LN");
            Console.ResetColor(); Console.Write(" lanterna  ");
            Console.ForegroundColor = ConsoleColor.Magenta; Console.Write("FS");
            Console.ResetColor(); Console.Write(" fusível  ");
            Console.ForegroundColor = ConsoleColor.White; Console.Write("CR");
            Console.ResetColor(); Console.Write(" medalhão  ");
            Console.ForegroundColor = ConsoleColor.Green; Console.Write("[]");
            Console.ResetColor(); Console.Write(" armário  ");
            Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("{}");
            Console.ResetColor(); Console.Write(" caixa de luzes");
        }

        static void DrawAll()
        {
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Cols; x++)
                    DrawTile(x, y);
        }

        static void DrawFloor(int x, int y)
        {
            if (Glyph.TryGetValue((x, y), out var g))
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write(g);
            }
            else if (RoomAt(x, y) >= 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkBlue;
                Console.Write(". ");
            }
            else
            {
                Console.Write("  ");
            }
        }

        // Só o cenário (sem itens nem monstro). Usado para zonas já exploradas.
        static void DrawTerrain(int x, int y)
        {
            char t = Map[x, y];
            if (t == '#')
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("##");
            }
            else if (t == 'D')
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.Write("||");
            }
            else if (t == 'E')
            {
                Console.ForegroundColor = hasKey ? ConsoleColor.Green : ConsoleColor.DarkYellow;
                Console.Write("EX");
            }
            else
            {
                DrawFloor(x, y);
            }
        }

        // Cada célula ocupa 2 caracteres para o mapa não ficar esticado na vertical.
        static void DrawTile(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Cols || y >= Rows) return;

            Console.SetCursorPosition(1 + x * 2, MazeTop + y);
            char t = Map[x, y];

            if (x == px && y == py)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("@@");
            }
            else if (!Vis[x, y])
            {
                // fora do campo de visão
                if (RememberMap && Seen[x, y]) DrawTerrain(x, y);
                else Console.Write("  ");
            }
            else if (monsterAlive && x == mx && y == my && !commsHide)
            {
                Console.ForegroundColor = monsterAwake ? ConsoleColor.Red : ConsoleColor.DarkRed;
                Console.Write("MM");
            }
            else if (t == 'L' && hasLamp)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("[]");
            }
            else if (!hasLamp && x == LampPos.x && y == LampPos.y)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("LN");
            }
            else if (hasLamp && IsFuseVisible(x, y))
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.Write("FS");
            }
            else if (hasLamp && IsMedalVisible(x, y))
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("CR");
            }
            else if (x == FuseBoxPos.x && y == FuseBoxPos.y)
            {
                Console.ForegroundColor = lightsOn ? ConsoleColor.Green : ConsoleColor.Yellow;
                Console.Write("{}");
            }
            else
            {
                DrawTerrain(x, y);
            }
            Console.ResetColor();
        }

        static string Objective()
        {
            if (!hasLamp) return "Objetivo: encontra a lanterna (refeitório).";
            if (!lightsOn && !hasFuse) return "Objetivo: encontra o fusível (num canto isolado).";
            if (!lightsOn && hasFuse) return "Objetivo: leva o fusível à sala das luzes.";
            if (monsterAlive && !hasMedal) return "Objetivo: o monstro acordou! Apanha o medalhão na sala dele.";
            if (monsterAlive && hasMedal) return "Objetivo: toca no monstro com o medalhão!";
            return "Objetivo: vai à sala da saída e abre a porta!";
        }

        static void DrawHud()
        {
            Console.SetCursorPosition(0, 1);
            Console.ResetColor();
            Console.Write(" ITENS: ");
            WriteItem("Lanterna", hasLamp);
            WriteItem("Fusível", hasFuse);
            WriteItem("Medalhão", hasMedal);
            WriteItem("Chave", hasKey);
            Console.ResetColor();
            int r = RoomAt(px, py);
            string where = r >= 0 ? Rooms[r].name : "Corredor";
            Console.Write("  Capturas: " + captures + "  Local: " + where);
            Console.Write(new string(' ', 25));

            Console.SetCursorPosition(1, 2);
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(Objective().PadRight(2 * Cols));
            Console.ResetColor();
        }

        static void WriteItem(string name, bool has)
        {
            Console.ForegroundColor = has ? ConsoleColor.Green : ConsoleColor.DarkGray;
            Console.Write(name + (has ? "[X] " : "[ ] "));
        }

        static void WriteStatus(string message, ConsoleColor color)
        {
            Console.SetCursorPosition(1, StatusRow);
            Console.ForegroundColor = color;
            Console.Write(message.PadRight(2 * Cols));
            Console.ResetColor();
        }

        // Ecrã de vitória: mostra o código e fica em pausa até carregares ENTER.
        static void ShowEnding()
        {
            FlushKeys();
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("==============================================");
            Console.WriteLine("              ESCAPASTE DA NAVE!");
            Console.WriteLine("==============================================");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine(" Destrancaste a porta e fugiste do monstro.");
            Console.WriteLine(" Capturas: " + captures);
            Console.WriteLine();
            Console.Write(" O código é:  ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(SecretCode);
            Console.ResetColor();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(" Anota o código! [ENTER] para fechar o jogo");
            Console.ResetColor();

            while (Console.ReadKey(true).Key != ConsoleKey.Enter) { }
        }
    }
}
