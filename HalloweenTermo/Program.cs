using System;
using System.Linq;

namespace HalloweenTermo
{
    class Program
    {
        static void Main()
        {
            Console.Title = "TERMO - Halloween";

            string[] palavras = {
                "MORTE",
                "BRUXA",
                "TUMBA",
                "SUSTO",
                "VULTO",
                "RITOS",
                "OSSOS",
                "MEDOS",
                "CORVO",
                "VELAS"
            };

            Random random = new Random();
            string palavra = palavras[random.Next(palavras.Length)];

            int tentativas = 6;
            bool ganhou = false;

            Console.WriteLine("=================================");
            Console.WriteLine("        TERMO - HALLOWEEN");
            Console.WriteLine("=================================");

            Console.WriteLine("\nDescobre a palavra de 5 letras!");
            Console.WriteLine("Tens 6 tentativas.\n");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("VERDE = Letra na posicao certa");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("AMARELO = Letra na posicao errada");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("VERMELHO = Letra nao encontrada");
            Console.ResetColor();

            for (int t = 1; t <= tentativas; t++)
            {
                Console.WriteLine("\n------------------------------");
                Console.WriteLine("Tentativa " + t + "/" + tentativas);
                Console.Write("Introduz uma palavra: ");

                string entrada = Console.ReadLine();

                // Input fechado (EOF): sai em vez de ficar preso no loop
                if (entrada == null)
                    break;

                string resposta = entrada.Trim().ToUpper();

                if (resposta.Length != 5 || !resposta.All(char.IsLetter))
                {
                    Console.WriteLine("A palavra deve ter 5 letras (so letras)!");
                    t--;
                    continue;
                }

                // Primeiro identificar letras na posicao certa
                int[] estado = new int[5];
                bool[] usadas = new bool[5];

                for (int i = 0; i < 5; i++)
                {
                    if (resposta[i] == palavra[i])
                    {
                        estado[i] = 2;
                        usadas[i] = true;
                    }
                }

                // Depois identificar letras na posicao errada
                for (int i = 0; i < 5; i++)
                {
                    if (estado[i] == 2)
                        continue;

                    for (int j = 0; j < 5; j++)
                    {
                        if (!usadas[j] && resposta[i] == palavra[j])
                        {
                            estado[i] = 1;
                            usadas[j] = true;
                            break;
                        }
                    }
                }

                Console.WriteLine("\nRESULTADO:");

                for (int i = 0; i < 5; i++)
                {
                    if (estado[i] == 2)
                        Console.ForegroundColor = ConsoleColor.Green;
                    else if (estado[i] == 1)
                        Console.ForegroundColor = ConsoleColor.Yellow;
                    else
                        Console.ForegroundColor = ConsoleColor.Red;

                    Console.Write("[" + resposta[i] + "]");
                    Console.ResetColor();
                }

                Console.WriteLine();

                if (resposta == palavra)
                {
                    ganhou = true;
                    break;
                }
            }

            if (ganhou)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n================================");
                Console.WriteLine("       PARABENS! GANHASTE!");
                Console.WriteLine("================================");
                Console.WriteLine("Codigo para o PC principal: 7392");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\nFICAS-TE SEM TENTATIVAS!");
                Console.WriteLine("A palavra era: " + palavra);
            }

            Console.ResetColor();
            Console.WriteLine("\nPrime ENTER para sair.");
            Console.ReadLine();
        }
    }
}