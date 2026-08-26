using YO4X.StrategyInputProjection;

string[] arguments = Environment.GetCommandLineArgs()[1..];
return await StrategyInputProjectionCommand.RunAsync(arguments).ConfigureAwait(false);
