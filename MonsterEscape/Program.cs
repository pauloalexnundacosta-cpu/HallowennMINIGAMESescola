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

        const int PlayerMoveMs = 90;      // intervalo entre passos do jogador

        // Monstro: o intervalo começa em MonsterBaseMs (MAIOR que o do jogador = mais lento)
        // e vai diminuindo MonsterRampPerSec milissegundos por cada segundo desde que acordou,
        // até ao mínimo MonsterMinMs. Com 0.2 ms/s apanha o ritmo do jogador ao fim de ~300 s
        // e passa a ser mais rápido do que ele a partir daí.
        const int MonsterBaseMs = PlayerMoveMs + 60;   // 150 ms: sempre mais lento que o jogador à partida
        const int MonsterMinMs = 80;
        const double MonsterRampPerSec = 0.2;

        // Visão do monstro: 360º, bloqueada por paredes e portas fechadas.
        // 12 apanha uma sala inteira (o refeitório, a maior, tem 11x7 = ~11.7 na diagonal).
        const double MonsterSightRange = 12;
        // Tempo que o jogador tem de ficar no armário para o monstro o perder de vista
        // e voltar à rota de patrulha.
        const int LoseSightMs = 5000;

        const int WakeGraceMs = 2500;      // tempo até o monstro começar a mexer depois das luzes voltarem
        const int CaughtPauseMs = 2000;    // pausa depois de seres apanhado

        // Tarefas (desbloqueiam a porta da sala do monstro)
        const int ReactorHoldMs = 5000;    // tempo parado na consola do reator
        const int SequenceLength = 4;      // tamanho da sequência de setas da antena
        const int SequenceShowMs = 2500;   // tempo que a sequência fica visível

        // Campo de visão
        const int SightCircleDark = 2;     // raio de visão sem lanterna
        const int SightCircleLamp = 2;     // raio à volta do jogador com lanterna
        const int SightCircleLit = 3;      // raio com as luzes ligadas
        const double ConeRange = 9;        // alcance do cone da lanterna
        const double ConeHalfAngleDeg = 45;// meio-ângulo do cone da lanterna (45 = cone de 90º)

        // Com as luzes ligadas: 180º à frente e alcance para apanhar uma sala inteira.
        // A maior sala (refeitório, 11x7) tem ~11.7 de ponta a ponta, por isso 14 dá margem.
        const double ConeRangeLit = 14;
        const double ConeHalfAngleLitDeg = 90; // 90 de meio-ângulo = 180º no total

        const bool RememberMap = false;    // true = as paredes/salas já vistas ficam desenhadas (sem itens nem monstro)
        // ==================================================================

        const int Cols = 59;
        const int Rows = 21;
        const int MazeTop = 4;
        const int StatusRow = MazeTop + Rows + 1;

        // O epsilon evita que o erro de vírgula flutuante deixe de fora as células exatamente
        // ao lado do jogador (cos(90º) dá ~6e-17 e não 0, o que cortava os 180º).
        static double ConeCosFor(double halfAngleDeg) => Math.Cos(halfAngleDeg * Math.PI / 180.0) - 1e-9;
        static readonly double ConeCos = ConeCosFor(ConeHalfAngleDeg);
        static readonly double ConeCosLit = ConeCosFor(ConeHalfAngleLitDeg);

        static readonly Random Rng = new Random();
        static readonly char[,] Map = new char[Cols, Rows];   // '#' parede, ' ' chão, 'L' armário, 'D' porta do monstro, 'E' porta da saída
        static readonly Dictionary<(int, int), string> Glyph = new Dictionary<(int, int), string>();
        static readonly (int dx, int dy)[] Dirs4 = { (0, -1), (1, 0), (0, 1), (-1, 0) };
        static readonly string[] SeqNames = { "^", ">", "v", "<" };

        static bool[,] Vis = new bool[Cols, Rows];            // o que o jogador vê agora
        static readonly bool[,] Seen = new bool[Cols, Rows];  // o que já viu alguma vez

        // Salas: nome (HUD), etiqueta (mapa), x1, y1, x2, y2
        // Ciclo: casa de banho -> refeitório -> comunicações -> luzes -> casa de banho.
        // Cadeia de tarefas (becos): refeitório -> Mad Bay -> enfermaria -> navegação -> reator.
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
            ("Enfermaria",      "ENFERMARIA",     31,  2, 39,  6),
            ("Navegação",       "NAVEGACAO",      48,  2, 57,  6),
            ("Reator",          "REATOR",         48, 10, 57, 14),
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

        // Tarefas
        static readonly (int x, int y) ReactorPos = (52, 12);     // tarefa 1: consola do reator (ficar parado)
        static readonly (int x, int y) CardPos = (38, 5);         // tarefa 2: cartão na enfermaria...
        static readonly (int x, int y) CommTermPos = (26, 19);    //           ...usado no terminal das comunicações
        static readonly (int x, int y) NavTermPos = (55, 4);      // tarefa 3: antena (sequência de setas)

        // Comportamento do monstro:
        //   Patrol = segue a rota default por todas as salas (só persegue se te vir)
        //   Chase  = viu-te: persegue-te pelo caminho mais curto, como se soubesse sempre onde estás
        //   Wander = perdeu-te (escondeste-te num armário): anda ao acaso até desistir
        enum MonsterMode { Patrol, Chase, Wander }

        // Estado
        static int px, py, mx, my, lastMx, lastMy;
        static int facX = 1, facY = 0;
        static bool hasLamp, hasFuse, lightsOn, hasMedal, hasKey, hasCard;
        static bool questReactor, questCard, questNav;
        static bool monsterAwake, monsterAlive = true, escaped, commsHide;
        static int captures;
        static Stopwatch sw;
        static long nextMonster, wakeStart;
        static long reactorHold = -1;
        static int reactorPct = -1;
        static int hudLen;

        static MonsterMode monsterMode = MonsterMode.Patrol;
        static readonly List<(int x, int y)> PatrolRoute = new List<(int x, int y)>();
        static int patrolIdx;      // próximo ponto da rota para onde o monstro vai
        static long wanderStart;   // instante em que o monstro perdeu o jogador de vista

        static void Main()
        {
            if (SecretCode.Length != 3)
                throw new InvalidOperationException("SecretCode tem de ter exatamente 3 caracteres.");

            Console.OutputEncoding = Encoding.UTF8;
            TryResizeConsole();
            try { Console.CursorVisible = false; } catch { }

            ShowIntro();
            BuildMap();
            BuildPatrolRoute();

            Console.Clear();
            Play();

            ShowEnding();
            try { Console.CursorVisible = true; } catch { }
        }

        static void TryResizeConsole()
        {
            try
            {
                int w = 2 * Cols + 2, h = StatusRow + 3;
                if (Console.WindowWidth >= w && Console.WindowHeight >= h) return;

                Console.SetBufferSize(Math.Max(Console.BufferWidth, w), Math.Max(Console.BufferHeight, h));
                Console.SetWindowSize(Math.Max(Console.WindowWidth, w), Math.Max(Console.WindowHeight, h));
            }
            catch { /* consola sem suporte: o aviso da introdução trata disto */ }
        }

        // ============================== JOGO ==============================
        static int QuestsDone()
        {
            return (questReactor ? 1 : 0) + (questCard ? 1 : 0) + (questNav ? 1 : 0);
        }

        static void Play()
        {
            px = StartPos.x; py = StartPos.y;
            mx = MonsterHome.x; my = MonsterHome.y;
            lastMx = mx; lastMy = my;
            monsterMode = MonsterMode.Patrol;
            patrolIdx = 0;

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

                // --- tarefa do reator (ficar parado) ---
                UpdateReactor(sw.ElapsedMilliseconds);

                // --- monstro ---
                now = sw.ElapsedMilliseconds;
                if (monsterAlive && monsterAwake && now >= wakeStart)
                    UpdateMonsterMode(now);   // viu-te? perdeu-te de vista? desistiu?

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
                if (!lightsOn)
                    WriteStatus("A porta da sala do monstro não abre. Sem luz, não vais entrar ali.", ConsoleColor.Red);
                else
                    WriteStatus("A porta da sala do monstro está selada! Faz as tarefas (" + QuestsDone() + "/3) para a destrancar.", ConsoleColor.Red);
                return false;
            }

            if (t == 'E')
            {
                if (hasKey) { escaped = true; return false; }
                WriteStatus("A porta da saída está trancada. A chave está no pescoço do monstro.", ConsoleColor.Red);
                return false;
            }

            // Sem lanterna não se encontram armários, fusível, cartão nem medalhão.
            if (!hasLamp && (t == 'L' || IsFuseVisible(nx, ny) || IsCardVisible(nx, ny) || IsMedalVisible(nx, ny)))
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
            else if (IsCardVisible(px, py) && hasLamp)
            {
                hasCard = true;
                WriteStatus("Cartão de acesso! Leva-o ao terminal da sala de comunicações.", ConsoleColor.Cyan);
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
                    monsterMode = MonsterMode.Patrol;   // acorda e começa a patrulhar (só te persegue se te vir)
                    patrolIdx = 0;
                    UpdateDoor();   // fica selada até as 3 tarefas estarem feitas
                    DrawAll();
                    wakeStart = now + WakeGraceMs;
                    nextMonster = wakeStart;
                    WriteStatus(QuestsDone() >= 3
                        ? "A LUZ VOLTOU! Ele acordou e saiu do esconderijo! A porta dele continua destrancada."
                        : "A LUZ VOLTOU! Ele acordou e saiu do esconderijo! A porta selou-se: faz as 3 tarefas.",
                        ConsoleColor.Red);
                }
                else if (!lightsOn)
                {
                    WriteStatus("A caixa de luzes está sem fusível.", ConsoleColor.Gray);
                }
            }
            else if (px == CommTermPos.x && py == CommTermPos.y)
            {
                if (!lightsOn)
                    WriteStatus("O terminal das comunicações está sem energia.", ConsoleColor.Gray);
                else if (questCard)
                    WriteStatus("O terminal já validou o teu cartão.", ConsoleColor.Gray);
                else if (hasCard)
                {
                    hasCard = false;
                    questCard = true;
                    CompleteQuest("Cartão validado no terminal!");
                }
                else
                    WriteStatus("Terminal bloqueado: precisa de um cartão de acesso (enfermaria).", ConsoleColor.Yellow);
            }
            else if (px == NavTermPos.x && py == NavTermPos.y)
            {
                if (!lightsOn)
                    WriteStatus("O terminal de navegação está sem energia.", ConsoleColor.Gray);
                else if (questNav)
                    WriteStatus("A antena já está alinhada.", ConsoleColor.Gray);
                else
                {
                    long t0 = sw.ElapsedMilliseconds;
                    bool ok = PlaySequenceGame();
                    long dt = sw.ElapsedMilliseconds - t0;
                    nextMonster += dt;   // o tempo pára enquanto usas o terminal
                    wakeStart += dt;
                    wanderStart += dt;
                    FlushKeys();

                    if (ok)
                    {
                        questNav = true;
                        CompleteQuest("Antena alinhada!");
                    }
                    else
                    {
                        WriteStatus("Abandonaste o terminal. Volta quando estiveres pronto.", ConsoleColor.Gray);
                    }
                }
            }
            else if (px == ReactorPos.x && py == ReactorPos.y)
            {
                if (!lightsOn)
                    WriteStatus("O reator está sem energia.", ConsoleColor.Gray);
                else if (questReactor)
                    WriteStatus("O reator já está estável.", ConsoleColor.Gray);
                // se houver trabalho a fazer, UpdateReactor mostra o progresso
            }
            else if (Map[px, py] == 'L' && hasLamp)
            {
                // Se o monstro te estava a perseguir, UpdateMonsterMode escreve logo a seguir
                // a mensagem de que te perdeu de vista.
                WriteStatus(monsterAwake
                    ? "Estás escondido no armário. O monstro não te consegue apanhar aqui."
                    : "Estás dentro do armário.", ConsoleColor.Green);
            }

            UpdateVision();
            DrawHud();
        }

        // Tarefa 1: ficar parado na consola do reator até a barra encher.
        static void UpdateReactor(long now)
        {
            if (questReactor) return;

            bool on = lightsOn && px == ReactorPos.x && py == ReactorPos.y;
            if (!on)
            {
                if (reactorHold >= 0)
                {
                    reactorHold = -1;
                    reactorPct = -1;
                    WriteStatus("Reator interrompido! Tens de ficar parado na consola.", ConsoleColor.Red);
                }
                return;
            }

            if (reactorHold < 0) { reactorHold = now; reactorPct = -1; }

            int pct = (int)Math.Min(100, (now - reactorHold) * 100 / ReactorHoldMs);
            int step = pct / 10 * 10;
            if (step != reactorPct)
            {
                reactorPct = step;
                int filled = step / 10;
                WriteStatus("A estabilizar o reator... [" + new string('#', filled) + new string('.', 10 - filled) + "] " + step + "%  (não saias da consola!)",
                            ConsoleColor.Yellow);
            }

            if (pct >= 100)
            {
                questReactor = true;
                reactorHold = -1;
                reactorPct = -1;
                CompleteQuest("Reator estabilizado!");
            }
        }

        // Tarefa 3: repetir uma sequência de setas. O jogo fica parado durante o minijogo.
        static int ArrowIndex(ConsoleKey k)
        {
            switch (k)
            {
                case ConsoleKey.UpArrow:
                case ConsoleKey.W: return 0;
                case ConsoleKey.RightArrow:
                case ConsoleKey.D: return 1;
                case ConsoleKey.DownArrow:
                case ConsoleKey.S: return 2;
                case ConsoleKey.LeftArrow:
                case ConsoleKey.A: return 3;
                default: return -1;
            }
        }

        static string SeqText(int[] seq, int count)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < count; i++) sb.Append(SeqNames[seq[i]]).Append(' ');
            return sb.ToString();
        }

        static bool PlaySequenceGame()
        {
            while (true)
            {
                var seq = new int[SequenceLength];
                for (int i = 0; i < seq.Length; i++) seq[i] = Rng.Next(4);

                WriteStatus("ANTENA (o tempo pára): memoriza  " + SeqText(seq, seq.Length), ConsoleColor.Yellow);
                Thread.Sleep(SequenceShowMs);
                FlushKeys();

                int ok = 0;
                bool failed = false;
                WriteStatus("Repete com as setas (ESC sai):  ", ConsoleColor.Gray);

                while (ok < seq.Length)
                {
                    var k = Console.ReadKey(true).Key;
                    if (k == ConsoleKey.Escape) return false;

                    int idx = ArrowIndex(k);
                    if (idx < 0) continue;

                    if (idx == seq[ok])
                    {
                        ok++;
                        WriteStatus("Repete com as setas (ESC sai):  " + SeqText(seq, ok), ConsoleColor.Green);
                    }
                    else
                    {
                        WriteStatus("Sequência errada! Vai aparecer outra...", ConsoleColor.Red);
                        Thread.Sleep(1000);
                        failed = true;
                        break;
                    }
                }

                if (!failed) return true;
            }
        }

        static void CompleteQuest(string msg)
        {
            int n = QuestsDone();
            if (n >= 3)
            {
                UpdateDoor();
                WriteStatus(msg + " Última tarefa! A porta da sala do monstro destrancou!", ConsoleColor.Green);
            }
            else
            {
                WriteStatus(msg + " Tarefas: " + n + "/3.", ConsoleColor.Green);
            }
            DrawHud();
        }

        // A porta só abre para o jogador com as luzes ligadas E as 3 tarefas feitas.
        // (O monstro atravessa-a sempre: é a casa dele.)
        static void UpdateDoor()
        {
            Map[DoorMonster.x, DoorMonster.y] = (lightsOn && QuestsDone() >= 3) ? ' ' : 'D';
            DrawTile(DoorMonster.x, DoorMonster.y);
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
            // As tarefas já concluídas mantêm-se.
            px = RespawnPos.x; py = RespawnPos.y;
            mx = MonsterHome.x; my = MonsterHome.y;
            lastMx = mx; lastMy = my;
            facX = 1; facY = 0;
            lightsOn = false;
            hasFuse = false;
            monsterAwake = false;
            monsterMode = MonsterMode.Patrol;
            patrolIdx = 0;
            commsHide = false;
            reactorHold = -1;
            reactorPct = -1;
            UpdateDoor();

            ResetVision();
            DrawAll();
            DrawHud();
            WriteStatus("Acordas no armazém. As luzes apagaram-se. Encontra o fusível de novo!", ConsoleColor.Gray);
            FlushKeys();
        }

        // ============================ MONSTRO (IA) ============================
        // Chamado a cada ciclo do jogo: decide em que modo o monstro está.
        static void UpdateMonsterMode(long now)
        {
            switch (monsterMode)
            {
                case MonsterMode.Patrol:
                    if (MonsterSeesPlayer()) StartChase();
                    break;

                case MonsterMode.Chase:
                    // Escondeste-te num armário: perde o rasto e começa a andar ao acaso.
                    if (Map[px, py] == 'L') StartWander(now);
                    break;

                case MonsterMode.Wander:
                    if (MonsterSeesPlayer()) StartChase();   // saíste do armário à vista dele
                    else if (now - wanderStart >= LoseSightMs) StartPatrol();
                    break;
            }
        }

        // Visão a 360º: alcance limitado e paredes/portas fechadas bloqueiam.
        // Quem está dentro de um armário não é visto.
        static bool MonsterSeesPlayer()
        {
            if (Map[px, py] == 'L') return false;

            int dx = px - mx, dy = py - my;
            if (dx * dx + dy * dy > MonsterSightRange * MonsterSightRange) return false;

            return HasLos(mx, my, px, py);
        }

        static void StartChase()
        {
            monsterMode = MonsterMode.Chase;
            WriteStatus("O MONSTRO VIU-TE! Ele sabe sempre onde estás. Corre ou esconde-te num armário!", ConsoleColor.Red);
        }

        static void StartWander(long now)
        {
            monsterMode = MonsterMode.Wander;
            wanderStart = now;
            WriteStatus("Escondeste-te! O monstro perdeu-te e anda ao acaso. Fica " + LoseSightMs / 1000 + " s no armário.", ConsoleColor.Yellow);
        }

        static void StartPatrol()
        {
            monsterMode = MonsterMode.Patrol;   // retoma a rota no ponto para onde ia (patrolIdx)
            WriteStatus("O monstro desistiu de te procurar e voltou à patrulha.", ConsoleColor.DarkYellow);
        }

        // Patrol: segue a rota. Chase: caminho mais curto até ao jogador.
        // Wander (ou sem caminho): anda ao acaso.
        // O monstro só anda no chão (e na porta da sala dele), por isso nunca entra num armário.
        static void MoveMonster()
        {
            int bx = mx, by = my;
            bool moved = false;

            if (monsterMode == MonsterMode.Chase)
                moved = StepToward(px, py, ref bx, ref by);
            else if (monsterMode == MonsterMode.Patrol)
                moved = StepAlongRoute(ref bx, ref by);

            if (!moved)
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

        // Um passo pelo caminho mais curto até (tx, ty). Devolve false se não houver caminho.
        static bool StepToward(int tx, int ty, ref int bx, ref int by)
        {
            var dist = Bfs(tx, ty);
            int best = dist[mx, my];
            if (best < 0) return false;

            bool moved = false;
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
            return moved;
        }

        // Um passo em direção ao ponto atual da rota; ao chegar, passa ao seguinte (rota circular).
        static bool StepAlongRoute(ref int bx, ref int by)
        {
            if (PatrolRoute.Count == 0) return false;

            var target = PatrolRoute[patrolIdx];
            if (mx == target.x && my == target.y)
            {
                patrolIdx = (patrolIdx + 1) % PatrolRoute.Count;
                target = PatrolRoute[patrolIdx];
            }
            return StepToward(target.x, target.y, ref bx, ref by);
        }

        // O monstro anda no chão e atravessa a porta da própria sala ('D').
        static bool CanMonsterWalk(int x, int y)
        {
            return x >= 0 && y >= 0 && x < Cols && y < Rows && (Map[x, y] == ' ' || Map[x, y] == 'D');
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

        // O cartão só aparece depois de as luzes voltarem.
        static bool IsCardVisible(int x, int y)
        {
            return lightsOn && !hasCard && !questCard && x == CardPos.x && y == CardPos.y;
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
            double coneCos = lightsOn ? ConeCosLit : ConeCos;
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
                            inCone = dot >= coneCos;
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

            // Corredores estreitos (1 de largura).
            Carve(5, 7, 5, 9);       // armazém        <-> casa de banho
            Carve(5, 15, 5, 16);     // casa de banho  <-> sala das luzes
            Carve(10, 12, 16, 12);   // casa de banho  <-> refeitório
            Carve(22, 6, 22, 7);     // refeitório     <-> Mad Bay
            Carve(22, 15, 22, 15);   // refeitório     <-> comunicações
            Carve(28, 10, 35, 10);   // refeitório     <-> saída
            Carve(28, 18, 34, 18);   // comunicações   <-> sala do monstro
            Carve(10, 17, 16, 17);   // sala das luzes <-> comunicações (2.ª entrada de ambas)
            Carve(28, 3, 30, 3);     // Mad Bay        <-> enfermaria
            Carve(40, 3, 47, 3);     // enfermaria     <-> navegação
            Carve(52, 7, 52, 9);     // navegação      <-> reator

            Map[DoorExit.x, DoorExit.y] = 'E';
            Map[DoorMonster.x, DoorMonster.y] = 'D';

            // Armários: Mad Bay, sala das luzes, enfermaria e reator
            Map[26, 5] = 'L';
            Map[9, 19] = 'L';
            Map[32, 5] = 'L';
            Map[57, 14] = 'L';
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

        // Rota default do monstro: um ponto (o centro) em cada sala que ele consegue alcançar,
        // ordenados pelo vizinho mais próximo a partir da casa dele. É calculada a partir do mapa,
        // por isso adapta-se sozinha se salas ou corredores forem alterados.
        // (A sala da saída fica de fora: está atrás da porta 'E', que o monstro não atravessa.)
        static void BuildPatrolRoute()
        {
            PatrolRoute.Clear();

            var reach = Bfs(MonsterHome.x, MonsterHome.y);
            var pending = new List<(int x, int y)>();
            for (int i = 0; i < Rooms.Length; i++)
            {
                var c = RoomCenter(i);
                if (c.x >= 0 && reach[c.x, c.y] >= 0) pending.Add(c);
            }

            var cur = MonsterHome;
            while (pending.Count > 0)
            {
                var d = Bfs(cur.x, cur.y);
                int best = 0;
                for (int i = 1; i < pending.Count; i++)
                    if (d[pending[i].x, pending[i].y] < d[pending[best].x, pending[best].y]) best = i;

                cur = pending[best];
                PatrolRoute.Add(cur);
                pending.RemoveAt(best);
            }

            patrolIdx = 0;
        }

        // Centro da sala; se estiver ocupado (armário, por exemplo) usa o chão livre mais próximo.
        // Devolve (-1, -1) se a sala não tiver nenhum chão onde o monstro possa andar.
        static (int x, int y) RoomCenter(int roomIdx)
        {
            var r = Rooms[roomIdx];
            int cx = (r.x1 + r.x2) / 2, cy = (r.y1 + r.y2) / 2;
            if (CanMonsterWalk(cx, cy)) return (cx, cy);

            (int x, int y) best = (-1, -1);
            int bestD = int.MaxValue;
            for (int x = r.x1; x <= r.x2; x++)
            {
                for (int y = r.y1; y <= r.y2; y++)
                {
                    if (!CanMonsterWalk(x, y)) continue;
                    int d = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                    if (d < bestD) { bestD = d; best = (x, y); }
                }
            }
            return best;
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
            Console.WriteLine(" Acordas numa nave às escuras. O monstro dorme na sala dele,");
            Console.WriteLine(" com a chave da saída pendurada ao pescoço.");
            Console.WriteLine();
            Console.WriteLine(" 1. Apanha a lanterna no refeitório.");
            Console.WriteLine(" 2. Leva o fusível à sala das luzes. As luzes voltam e o");
            Console.WriteLine("    monstro ACORDA e sai do esconderijo.");
            Console.WriteLine(" 3. A porta dele fica selada: faz as 3 tarefas (reator,");
            Console.WriteLine("    cartão da enfermaria e antena de navegação).");
            Console.WriteLine(" 4. Apanha o medalhão e toca no monstro para ficares com a chave.");
            Console.WriteLine(" 5. Vai à sala da saída, abre a porta e foge.");
            Console.WriteLine();
            Console.WriteLine(" - Só vês o que está no teu campo de visão (vira-te para olhar).");
            Console.WriteLine(" - Sem lanterna não encontras armários, fusível nem medalhão.");
            Console.WriteLine(" - Os armários escondem-te. Nas comunicações não vês o monstro.");
            Console.WriteLine(" - O monstro patrulha a nave e vê a 360º (alcance de uma sala).");
            Console.WriteLine("   Se te vir, persegue-te sempre. Esconde-te num armário " + LoseSightMs / 1000 + " s para o despistar.");
            Console.WriteLine(" - O monstro começa mais lento que tu, mas acelera com o tempo.");
            Console.WriteLine(" - Se ele te apanhar, acordas no armazém e tens de refazer o");
            Console.WriteLine("   fusível (as tarefas já feitas mantêm-se).");
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
            Console.ForegroundColor = ConsoleColor.Cyan; Console.Write("CT");
            Console.ResetColor(); Console.Write(" cartão  ");
            Console.ForegroundColor = ConsoleColor.White; Console.Write("CR");
            Console.ResetColor(); Console.Write(" medalhão  ");
            Console.ForegroundColor = ConsoleColor.Green; Console.Write("[]");
            Console.ResetColor(); Console.Write(" armário  ");
            Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("{}");
            Console.ResetColor(); Console.Write(" luzes  ");
            Console.ForegroundColor = ConsoleColor.Yellow; Console.Write("RE TC NV");
            Console.ResetColor(); Console.Write(" tarefas");
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

        static ConsoleColor TaskColor(bool done)
        {
            return done ? ConsoleColor.Green : (lightsOn ? ConsoleColor.Yellow : ConsoleColor.DarkGray);
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
            else if (hasLamp && IsCardVisible(x, y))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("CT");
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
            else if (x == ReactorPos.x && y == ReactorPos.y)
            {
                Console.ForegroundColor = TaskColor(questReactor);
                Console.Write("RE");
            }
            else if (x == CommTermPos.x && y == CommTermPos.y)
            {
                Console.ForegroundColor = TaskColor(questCard);
                Console.Write("TC");
            }
            else if (x == NavTermPos.x && y == NavTermPos.y)
            {
                Console.ForegroundColor = TaskColor(questNav);
                Console.Write("NV");
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
            if (!lightsOn) return "Objetivo: leva o fusível à sala das luzes.";
            if (monsterAlive && hasMedal) return "Objetivo: toca no monstro com o medalhão!";

            if (monsterAlive && QuestsDone() < 3)
            {
                var pending = new List<string>();
                if (!questReactor) pending.Add("reator");
                if (!questCard) pending.Add(hasCard ? "terminal das comunicações" : "cartão (enfermaria)");
                if (!questNav) pending.Add("antena (navegação)");
                return "Objetivo: ele saiu do esconderijo! Tarefas (" + QuestsDone() + "/3): " + string.Join(", ", pending);
            }

            if (monsterAlive) return "Objetivo: a porta destrancou! Apanha o medalhão na sala do monstro.";
            return "Objetivo: vai à sala da saída e abre a porta!";
        }

        static void HudWrite(string s, ConsoleColor c)
        {
            Console.ForegroundColor = c;
            Console.Write(s);
            hudLen += s.Length;
        }

        static void HudItem(string name, bool has)
        {
            HudWrite(name + (has ? "[X] " : "[ ] "), has ? ConsoleColor.Green : ConsoleColor.DarkGray);
        }

        static void DrawHud()
        {
            Console.SetCursorPosition(0, 1);
            hudLen = 0;
            HudWrite(" ITENS: ", ConsoleColor.Gray);
            HudItem("Lanterna", hasLamp);
            HudItem("Fusível", hasFuse);
            HudItem("Cartão", hasCard);
            HudItem("Medalhão", hasMedal);
            HudItem("Chave", hasKey);

            int r = RoomAt(px, py);
            string where = r >= 0 ? Rooms[r].name : "Corredor";
            HudWrite(" Tarefas: " + QuestsDone() + "/3  Capturas: " + captures + "  Local: " + where, ConsoleColor.Gray);
            Console.ResetColor();
            Console.Write(new string(' ', Math.Max(0, 2 * Cols - hudLen)));

            Console.SetCursorPosition(1, 2);
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(Objective().PadRight(2 * Cols));
            Console.ResetColor();
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