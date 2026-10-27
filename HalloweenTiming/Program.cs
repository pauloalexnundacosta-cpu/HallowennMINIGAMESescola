using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace HalloweenTiming
{
    internal static class Program
    {
        // ============ CONFIGURAÇÃO (ajusta aqui a dificuldade) ============
        const string SecretCode = "7K3";                       // exatamente 3 caracteres
        const int BarWidth = 40;                               // largura da barra (células)
        const int HitsRequired = 5;                            // acertos necessários por selo
        const bool ResetOnMiss = false;                        // true = falhar zera os acertos do selo (muito mais difícil)
        static readonly int[] ZoneWidths = { 7, 5, 4 };        // tamanho da zona alvo por selo
        static readonly double[] Speeds = { 50, 80, 100 };      // velocidade do cursor (células/seg) por selo
        const int PenaltyMs = 1200;                            // pausa após falhar
        // ==================================================================

        static readonly Random Rng = new Random();
        static readonly char[] Revealed = { '_', '_', '_' };
        static int barRow;

        static void Main()
        {
            if (SecretCode.Length != 3)
                throw new InvalidOperationException("SecretCode tem de ter exatamente 3 caracteres.");

            Console.OutputEncoding = Encoding.UTF8;
            try { Console.CursorVisible = false; } catch { /* consola sem suporte */ }

            ShowIntro();

            int attempts = 0;
            for (int round = 0; round < 3; round++)
            {
                int hits = 0;
                while (hits < HitsRequired)
                {
                    attempts++;
                    bool hit = Swing(round, hits, attempts);
                    if (hit) hits++;
                    else if (ResetOnMiss) hits = 0;
                }
                Revealed[round] = SecretCode[round];
            }

            ShowEnding();
            try { Console.CursorVisible = true; } catch { }
        }

        // ---------- Uma tentativa (um varrimento do cursor até ao jogador carregar) ----------
        static bool Swing(int round, int hits, int attempt)
        {
            int zoneWidth = ZoneWidths[round];
            double speed = Speeds[round];
            int zoneStart = Rng.Next(2, BarWidth - zoneWidth - 1);

            DrawHeader(round, hits, attempt);
            FlushKeys();

            var sw = Stopwatch.StartNew();
            int lastPos = -1;

            while (true)
            {
                int pos = PingPong(sw.Elapsed.TotalSeconds * speed);

                if (pos != lastPos)
                {
                    DrawBar(pos, zoneStart, zoneWidth);
                    lastPos = pos;
                }

                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key != ConsoleKey.Spacebar) continue;

                    int hitPos = PingPong(sw.Elapsed.TotalSeconds * speed);
                    DrawBar(hitPos, zoneStart, zoneWidth);

                    bool hit = hitPos >= zoneStart && hitPos < zoneStart + zoneWidth;
                    if (hit)
                    {
                        bool last = hits + 1 >= HitsRequired;
                        WriteStatus(last ? "SELO QUEBRADO! Caracter desbloqueado."
                                         : "ACERTO! (" + (hits + 1) + "/" + HitsRequired + ")",
                                    ConsoleColor.Green);
                        Thread.Sleep(last ? 1000 : 450);
                        return true;
                    }

                    WriteStatus(ResetOnMiss ? "FALHASTE! Perdes os acertos deste selo..."
                                            : "FALHASTE! Tenta outra vez...",
                                ConsoleColor.Red);
                    Thread.Sleep(PenaltyMs);
                    return false;
                }

                Thread.Sleep(1);
            }
        }

        // ---------- Lógica do cursor ----------
        // Converte tempo*velocidade numa posição que vai e volta entre 0 e BarWidth-1.
        static int PingPong(double distance)
        {
            double period = 2.0 * (BarWidth - 1);
            double d = distance % period;
            return d <= BarWidth - 1 ? (int)d : (int)(period - d);
        }

        static void FlushKeys()
        {
            while (Console.KeyAvailable) Console.ReadKey(true);
        }

        // ---------- Interface ----------
        static void ShowIntro()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("==============================================");
            Console.WriteLine("        FECHADURA AMALDIÇOADA  \\|/");
            Console.WriteLine("==============================================");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine(" A porta só abre com um código de 3 caracteres.");
            Console.WriteLine(" Cada caracter está preso por um selo.");
            Console.WriteLine();
            Console.WriteLine(" Carrega em ESPAÇO quando o cursor '|' estiver");
            Console.WriteLine(" dentro da zona '#'.");
            Console.WriteLine(" Para quebrar cada selo precisas de " + HitsRequired + " acertos.");
            Console.WriteLine();
            Console.WriteLine(" Os selos ficam cada vez mais rápidos.");
            Console.WriteLine(" Despacha-te... ele está a aproximar-se.");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(" [ESPAÇO] para começar");
            Console.ResetColor();

            while (true)
            {
                if (Console.ReadKey(true).Key == ConsoleKey.Spacebar) break;
            }
        }

        static void DrawHeader(int round, int hits, int attempt)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("=== SELO " + (round + 1) + " / 3 ===");
            Console.ResetColor();
            Console.WriteLine();

            Console.Write(" CÓDIGO: [ ");
            for (int i = 0; i < 3; i++)
            {
                Console.ForegroundColor = Revealed[i] == '_' ? ConsoleColor.DarkGray : ConsoleColor.Green;
                Console.Write(Revealed[i] + " ");
            }
            Console.ResetColor();
            Console.WriteLine("]");

            Console.Write(" ACERTOS: [");
            for (int i = 0; i < HitsRequired; i++)
            {
                if (i < hits)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write('*');
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write('.');
                }
            }
            Console.ResetColor();
            Console.WriteLine("]  " + hits + "/" + HitsRequired + "   (tentativa nº " + attempt + ")");
            Console.WriteLine();

            barRow = Console.CursorTop;
            Console.WriteLine(); // linha da barra
            Console.WriteLine(); // linha de estado
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(" [ESPAÇO] para travar o cursor");
            Console.ResetColor();
        }

        static void DrawBar(int pos, int zoneStart, int zoneWidth)
        {
            Console.SetCursorPosition(0, barRow);
            Console.Write(" [");
            for (int i = 0; i < BarWidth; i++)
            {
                if (i == pos)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write('|');
                }
                else if (i >= zoneStart && i < zoneStart + zoneWidth)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write('#');
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write('-');
                }
            }
            Console.ResetColor();
            Console.Write("]");
        }

        static void WriteStatus(string message, ConsoleColor color)
        {
            Console.SetCursorPosition(0, barRow + 1);
            Console.ForegroundColor = color;
            Console.Write(" " + message.PadRight(BarWidth + 2));
            Console.ResetColor();
        }

        static void ShowEnding()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("==============================================");
            Console.WriteLine("          TODOS OS SELOS QUEBRADOS");
            Console.WriteLine("==============================================");
            Console.ResetColor();
            Console.WriteLine();
            Console.Write(" O código é:  ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(SecretCode);
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine(" Corre para a porta!");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(" [qualquer tecla para sair]");
            Console.ResetColor();
            Console.ReadKey(true);
        }
    }
}