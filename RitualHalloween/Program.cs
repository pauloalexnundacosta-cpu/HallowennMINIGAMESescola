using System;
using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;

namespace RitualHalloween
{
    class Program
    {
        // ----- Configuração -----
        const int TotalRondas = 10;         // tamanho final da sequência
        const int TempoPorTecla = 2000;     // ms para carregar cada tecla (tensão!)

        static readonly Random Rng = new Random();

        static readonly string[] Nomes = { "Abóbora", "Fantasma", "Morcego", "Caveira" };

        static readonly ConsoleColor[] Cores =
        {
            ConsoleColor.DarkYellow, // abóbora (laranja)
            ConsoleColor.White,      // fantasma
            ConsoleColor.Magenta,    // morcego
            ConsoleColor.Red         // caveira
        };

        static readonly int[] Frequencias = { 262, 330, 392, 523 };

        // Arte de cada símbolo (5 linhas, 11 colunas)
        static readonly string[][] Arte =
        {
            new[] { // Abóbora
                @"    ,|,    ",
                @"  .-'''-.  ",
                @" ( ^   ^ ) ",
                @" (  \_/  ) ",
                @"  '-vvv-'  "
            },
            new[] { // Fantasma
                @"  .-'''-.  ",
                @" | o   o | ",
                @" |   O   | ",
                @" |       | ",
                @" |/\/\/\/| "
            },
            new[] { // Morcego
                @" /\     /\ ",
                @"/  \._./  \",
                @"\   (o o)  ",
                @" \   \v/  /",
                @"  \/\_/\/  "
            },
            new[] { // Caveira
                @"  .-----.  ",
                @" / () () \ ",
                @" \   ^   / ",
                @"  \|||||/  ",
                @"   '---'   "
            }
        };

        static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            try { Console.CursorVisible = false; } catch { }
            Console.Title = "Ritual das Abóboras";

            Intro();

            // ===== TELA DE LOADING (adicionada) =====
            Console.CursorVisible = false;
            Console.Title = "LOAD - Carregando...";

            MostrarBanner();
            CarregarBarraDeProgresso();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n\n      Carregamento concluído com sucesso!");
            Console.ResetColor();

            Thread.Sleep(1000);
            Console.Clear();
            Console.CursorVisible = true;
            // ===== FIM DA TELA DE LOADING =====

            // O jogo só termina quando o jogador completar todas as rondas.
            // Se falhar uma ronda, repete essa mesma ronda (não volta ao início).
            Jogar();

            Vitoria();
        }

        // ================== Ecrãs ==================

        static void Intro()
        {
            Console.Clear();
            Escrever("\n  ========================================\n", ConsoleColor.DarkYellow);
            Escrever("        R I T U A L   D A S   A B Ó B O R A S\n", ConsoleColor.DarkYellow);
            Escrever("  ========================================\n\n", ConsoleColor.DarkYellow);

            Escrever("  É noite de Halloween e a casa está assombrada...\n", ConsoleColor.Gray);
            Escrever("  Para quebrar a maldição, tens de repetir o ritual.\n\n", ConsoleColor.Gray);
            Escrever("  Os símbolos vão acender numa sequência.\n", ConsoleColor.Gray);
            Escrever("  Repete-a carregando nas teclas:\n\n", ConsoleColor.Gray);

            for (int i = 0; i < 4; i++)
                Escrever($"     [{i + 1}] {Nomes[i]}\n", Cores[i]);

            Escrever($"\n  Tens {TempoPorTecla / 1000} segundos por tecla. Se errares, repetes a mesma ronda!\n", ConsoleColor.DarkGray);
            Escrever("\n  Prime ENTER para começar...", ConsoleColor.White);
            EsperarEnter();
        }

        static void Derrota(int ronda)
        {
            Console.Clear();
            Escrever("\n\n   Os espíritos riem-se de ti...\n\n", ConsoleColor.Red);
            Escrever("     .-----.\n    / x   x \\\n    \\  ___  /\n     '|||||'\n\n", ConsoleColor.DarkRed);
            Escrever($"   Falhaste a ronda {ronda}/{TotalRondas}. A sequência é a mesma, tenta outra vez!\n", ConsoleColor.Gray);
            Escrever("\n   Prime ENTER para repetir esta ronda...", ConsoleColor.White);
            TocarSomErro();
            EsperarEnter();
        }

        static void Vitoria()
        {
            string codigo = "BPK";

            Console.Clear();
            Escrever("\n\n   A maldição foi quebrada! Os fantasmas desvanecem-se...\n\n", ConsoleColor.Green);
            Escrever("   O teu código secreto é:\n\n", ConsoleColor.Gray);

            Console.ForegroundColor = ConsoleColor.Black;
            Console.BackgroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("            " + string.Join(" ", codigo.ToCharArray()) + "            ");
            Console.ResetColor();

            Escrever("\n\n   (O código fica no ecrã até fechares com ENTER.)\n", ConsoleColor.DarkGray);
            Escrever("\n   Prime ENTER para sair...", ConsoleColor.White);

            // O programa só termina quando o jogador carregar ENTER,
            // por isso o código nunca desaparece sozinho.
            EsperarEnter();
        }

        // ================== Jogo ==================

        static void Jogar()
        {
            int[] sequencia = new int[TotalRondas];
            for (int i = 0; i < TotalRondas; i++)
                sequencia[i] = Rng.Next(4);

            for (int ronda = 1; ronda <= TotalRondas; ronda++)
            {
                // Enquanto falhar, repete a MESMA ronda com a MESMA sequência
                while (true)
                {
                    MostrarSequencia(sequencia, ronda);

                    if (ResponderRonda(sequencia, ronda))
                        break;

                    Derrota(ronda);
                }

                if (ronda < TotalRondas)
                {
                    Desenhar(-1, ronda, "Certo! Os espíritos aproximam-se...");
                    Thread.Sleep(900);
                }
            }
        }

        static void MostrarSequencia(int[] sequencia, int ronda)
        {
            // Fica mais rápido a cada ronda (ronda 1 = lento, ronda 13 = muito rápido)
            int luz = Math.Max(100, 600 - (ronda - 1) * 40);    // tempo aceso (ms)
            int pausa = Math.Max(60, 200 - (ronda - 1) * 12);   // pausa entre símbolos (ms)

            Desenhar(-1, ronda, "Observa com atenção...");
            Thread.Sleep(900);

            for (int i = 0; i < ronda; i++)
            {
                Desenhar(sequencia[i], ronda, "Observa com atenção...");
                TocarNota(sequencia[i], luz);
                Desenhar(-1, ronda, "Observa com atenção...");
                Thread.Sleep(pausa);
            }
        }

        // Devolve true se o jogador repetiu a sequência toda corretamente
        static bool ResponderRonda(int[] sequencia, int ronda)
        {
            LimparTeclado();
            for (int i = 0; i < ronda; i++)
            {
                Desenhar(-1, ronda, $"A tua vez! ({i + 1}/{ronda})");
                int escolha = LerSimbolo();

                if (escolha < 0) // tempo esgotado
                    return false;

                Desenhar(escolha, ronda, $"A tua vez! ({i + 1}/{ronda})");
                TocarNota(escolha, 200);

                if (escolha != sequencia[i])
                    return false;
            }
            return true;
        }

        // Lê 1-4 (teclado normal ou numérico). Devolve -1 se o tempo acabar.
        static int LerSimbolo()
        {
            Stopwatch relogio = Stopwatch.StartNew();

            while (relogio.ElapsedMilliseconds < TempoPorTecla)
            {
                if (Console.KeyAvailable)
                {
                    ConsoleKey tecla = Console.ReadKey(true).Key;
                    switch (tecla)
                    {
                        case ConsoleKey.D1: case ConsoleKey.NumPad1: return 0;
                        case ConsoleKey.D2: case ConsoleKey.NumPad2: return 1;
                        case ConsoleKey.D3: case ConsoleKey.NumPad3: return 2;
                        case ConsoleKey.D4: case ConsoleKey.NumPad4: return 3;
                    }
                }
                Thread.Sleep(10);
            }

            return -1;
        }

        // ================== Desenho ==================

        static void Desenhar(int aceso, int ronda, string mensagem)
        {
            Console.Clear();
            Escrever($"\n   RITUAL DAS ABÓBORAS   -   Ronda {ronda}/{TotalRondas}\n\n", ConsoleColor.DarkYellow);

            for (int linha = 0; linha < 5; linha++)
            {
                Console.Write("   ");
                for (int s = 0; s < 4; s++)
                {
                    if (s == aceso)
                    {
                        Console.BackgroundColor = Cores[s];
                        Console.ForegroundColor = ConsoleColor.Black;
                    }
                    else
                    {
                        Console.ResetColor();
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                    }

                    Console.Write(Arte[s][linha]);
                    Console.ResetColor();
                    Console.Write("  ");
                }
                Console.WriteLine();
            }

            Console.Write("   ");
            for (int s = 0; s < 4; s++)
            {
                Console.ForegroundColor = Cores[s];
                Console.Write(($"[{s + 1}] {Nomes[s]}").PadRight(13));
            }
            Console.ResetColor();

            Console.WriteLine("\n");
            Escrever("   " + mensagem + "\n", ConsoleColor.Gray);
        }

        // ================== Utilitários ==================

        static void Escrever(string texto, ConsoleColor cor)
        {
            Console.ForegroundColor = cor;
            Console.Write(texto);
            Console.ResetColor();
        }

        static void LimparTeclado()
        {
            while (Console.KeyAvailable)
                Console.ReadKey(true);
        }

        static void EsperarEnter()
        {
            LimparTeclado();
            while (Console.ReadKey(true).Key != ConsoleKey.Enter) { }
        }

        static void TocarNota(int simbolo, int duracao)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    Console.Beep(Frequencias[simbolo], duracao);
                else
                    Thread.Sleep(duracao);
            }
            catch { Thread.Sleep(duracao); }
        }

        static void TocarSomErro()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Console.Beep(200, 300);
                    Console.Beep(150, 500);
                }
            }
            catch { }
        }

        // ===== METODOS DA TELA DE LOADING (adicionados) =====

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

        }
    }
}