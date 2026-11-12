using MassTransit;
using Microsoft.EntityFrameworkCore;
using ZTP_lab02.Contracts;
using ZTP_lab02.EmailService;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")!;

// 1. Configure EF Core (PostgreSQL)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// 2. Configure MassTransit + RabbitMQ consumer for the Email channel
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<EmailConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var rabbitHost = builder.Configuration["RabbitMq:Host"] ?? "localhost";
        var rabbitUser = builder.Configuration["RabbitMq:Username"] ?? "guest";
        var rabbitPass = builder.Configuration["RabbitMq:Password"] ?? "guest";

        cfg.Host(rabbitHost, "/", h =>
        {
            h.Username(rabbitUser);
            h.Password(rabbitPass);
        });

        cfg.ReceiveEndpoint("email-queue", e =>
        {
            // Enable RabbitMQ queue priority (1 to 10) so High priority messages are processed first
            e.EnablePriority(10);
            e.ConfigureConsumer<EmailConsumer>(context);
        });
    });
});

var host = builder.Build();
host.Run();