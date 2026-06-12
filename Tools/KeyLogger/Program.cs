using System;

// Logs every key pressed in this console window to the terminal.
// Useful as a quick input-debugging tool during game development.
// Press Escape to quit.

Console.WriteLine("Press Any Key to See it logged.\n");

while (true) // spawn loop concept
{
    ConsoleKeyInfo key = Console.ReadKey(intercept: true);

    if (key.Key == ConsoleKey.Escape) break;

    Console.WriteLine($"Key: {key.Key,-20} Char: '{key.KeyChar}'");
}
