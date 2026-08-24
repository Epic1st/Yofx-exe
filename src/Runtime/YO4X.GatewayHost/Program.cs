using YO4X.GatewayHost;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGatewayUserOperationProtocol(builder.Configuration);
builder.Services.AddMt5ProcessBoundary(builder.Configuration);
builder.Services.AddBrokerCommandOneShot(builder.Configuration);

var app = builder.Build();

app.MapGatewayHostHealthEndpoints();

app.Run();

public partial class Program
{
}
