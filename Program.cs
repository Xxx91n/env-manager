using Microsoft.Win32;
using Spectre.Console;

if (args.Length == 0)
{
    ShowHelp();
    return;
}

switch (args[0])
{
    case "list":
        ListEnvironment();
        break;
    case "get":
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: env-manager get <name>");
            return;
        }
        GetVariable(args[1]);
        break;
    case "set":
        if (args.Length < 3)
        {
            Console.WriteLine("Usage: env-manager set <name> <value> [--scope user|system]");
            return;
        }
        string scope = "user";
        if (args.Length > 3 && args[3] == "--scope" && args.Length > 4)
            scope = args[4];
        SetVariable(args[1], args[2], scope);
        break;
    case "delete":
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: env-manager delete <name> [--scope user|system]");
            return;
        }
        scope = "user";
        if (args.Length > 2 && args[2] == "--scope" && args.Length > 3)
            scope = args[3];
        DeleteVariable(args[1], scope);
        break;
    case "help":
        ShowHelp();
        break;
    default:
        Console.WriteLine($"Unknown command: {args[0]}");
        ShowHelp();
        break;
}

void ListEnvironment()
{
    var items = new List<(string, string, string)>();
    
    // User scope
    using (var key = Registry.CurrentUser.OpenSubKey("Environment"))
    {
        if (key != null)
        {
            foreach (var name in key.GetValueNames())
            {
                var value = key.GetValue(name)?.ToString() ?? "";
                items.Add((name, "user", value));
            }
        }
    }
    
    // System scope
    try
    {
        using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"))
        {
            if (key != null)
            {
                foreach (var name in key.GetValueNames())
                {
                    var value = key.GetValue(name)?.ToString() ?? "";
                    items.Add((name, "system", value));
                }
            }
        }
    }
    catch { }
    
    var table = new Table();
    table.AddColumn("Name");
    table.AddColumn("Scope");
    table.AddColumn("Value");
    
    foreach (var (name, s, value) in items.OrderBy(x => x.Item1))
    {
        var displayValue = value.Length > 60 ? value.Substring(0, 57) + "..." : value;
        table.AddRow(name, s, displayValue);
    }
    
    AnsiConsole.Write(table);
}

void GetVariable(string name)
{
    // Check user scope first
    using (var key = Registry.CurrentUser.OpenSubKey("Environment"))
    {
        if (key != null)
        {
            var value = key.GetValue(name);
            if (value != null)
            {
                Console.WriteLine($"{name} = {value}");
                return;
            }
        }
    }
    
    // Check system scope
    try
    {
        using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"))
        {
            if (key != null)
            {
                var value = key.GetValue(name);
                if (value != null)
                {
                    Console.WriteLine($"{name} = {value}");
                    return;
                }
            }
        }
    }
    catch { }
    
    Console.WriteLine($"Variable '{name}' not found.");
}

void SetVariable(string name, string value, string scope)
{
    string path = scope.ToLower() == "system" 
        ? @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment" 
        : "Environment";
    
    RegistryHive hive = scope.ToLower() == "system" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
    
    try
    {
        RegistryKey baseKey = hive == RegistryHive.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine;
        using (var key = baseKey.OpenSubKey(path, true))
        {
            if (key != null)
            {
                key.SetValue(name, value);
                AnsiConsole.MarkupLine($"[green]✓ Set {name} = {value} [{scope}][/]");
            }
        }
    }
    catch (System.UnauthorizedAccessException)
    {
        AnsiConsole.MarkupLine($"[red]Error: Access denied. System scope requires elevation.[/]");
    }
}

void DeleteVariable(string name, string scope)
{
    string path = scope.ToLower() == "system" 
        ? @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment" 
        : "Environment";
    
    RegistryHive hive = scope.ToLower() == "system" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
    
    try
    {
        RegistryKey baseKey = hive == RegistryHive.CurrentUser ? Registry.CurrentUser : Registry.LocalMachine;
        using (var key = baseKey.OpenSubKey(path, true))
        {
            if (key != null)
            {
                key.DeleteValue(name, false);
                AnsiConsole.MarkupLine($"[green]✓ Deleted {name} [{scope}][/]");
            }
        }
    }
    catch (System.UnauthorizedAccessException)
    {
        AnsiConsole.MarkupLine($"[red]Error: Access denied. System scope requires elevation.[/]");
    }
}

void ShowHelp()
{
    Console.WriteLine(@"
Environment Variable Manager

Usage: env-manager <command> [options]

Commands:
  list                          List all environment variables
  get <name>                    Get a variable value
  set <name> <value> [--scope]  Set a variable (default: user scope)
  delete <name> [--scope]       Delete a variable
  help                          Show this help

Options:
  --scope user|system           Variable scope (default: user)

Examples:
  env-manager list
  env-manager get PATH
  env-manager set MY_VAR my_value
  env-manager set MY_VAR my_value --scope system
  env-manager delete MY_VAR
");
}
