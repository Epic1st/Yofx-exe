using YO4X.MarketData.Mt5Import;

if (Mt5TickImportCommand.IsRequested(args))
{
    return await Mt5TickImportCommand.RunAsync(args).ConfigureAwait(false);
}

Console.Error.WriteLine(Mt5TickImportCommand.Usage);
return 2;
