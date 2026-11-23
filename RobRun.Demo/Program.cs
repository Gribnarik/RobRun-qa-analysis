using RobRun.Demo.Scenarios;

Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
Console.WriteLine("║   Rob Run — Демонстрация серверных механик              ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
Console.WriteLine();

while (true)
{
    Console.WriteLine("Выберите проблему для демонстрации:");
    Console.WriteLine("  [1] Проблема 1: Асимметрия прав доступа в пачке");
    Console.WriteLine("  [2] Проблема 2: Изучение поставок привязано к игроку");
    Console.WriteLine("  [3] Проблема 3: Кланы — нулевая управляемость");
    Console.WriteLine("  [4] Проблема 4: Клановый дом и общий пул изучений");
    Console.WriteLine("  [0] Выход");
    Console.Write("\nВаш выбор: ");

    var choice = Console.ReadLine()?.Trim();

    switch (choice)
    {
        case "1":
            await Problem1Demo.Run();
            break;
        case "2":
            Console.WriteLine("⚠️  Проблема 2 ещё не реализована в демо");
            break;
        case "3":
            Console.WriteLine("⚠️  Проблема 3 ещё не реализована в демо");
            break;
        case "4":
            Console.WriteLine("⚠️  Проблема 4 ещё не реализована в демо");
            break;
        case "0":
            Console.WriteLine("До свидания!");
            return;
        default:
            Console.WriteLine("Неверный выбор. Попробуйте снова.\n");
            break;
    }

    Console.WriteLine("\n" + new string('═', 60) + "\n");
}