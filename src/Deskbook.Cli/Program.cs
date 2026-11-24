using Deskbook.Cli;

return await App.RunAsync(args, Console.Out, Console.Error, AppEnvironment.FromProcess());
