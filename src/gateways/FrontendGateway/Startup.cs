using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Middleware;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using System;
using System.Text;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Rewrite;

namespace FrontendGateway;

public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    private IConfiguration Configuration { get; }

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers(options =>
        {
            options.OutputFormatters.RemoveType<StringOutputFormatter>();
        });

        services.AddOcelot(Configuration);

        var jwtSection = Configuration.GetSection("jwt");
        var jwtOptions = jwtSection.Get<JwtOptions>();
        var key = Encoding.UTF8.GetBytes(jwtOptions.Secret);

        services.AddAuthentication(x =>
            {
                x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(x =>
            {
                x.RequireHttpsMetadata = false;
                x.SaveToken = true;
                x.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false
                };
            });

        services.AddCors(options =>
        {
            options.AddPolicy("CorsPolicy",
                builder => builder.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader());
        });

        services.AddHealthChecks()
            .AddMongoDb(
                mongodbConnectionString: (
                    Configuration.GetSection("mongo").Get<MongoOptions>()
                    ?? throw new Exception("mongo configuration section not found")
                ).ConnectionString,
                name: "mongo",
                failureStatus: HealthStatus.Unhealthy
            );

        services.AddHealthChecksUI().AddInMemoryStorage();
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public async void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseMiddleware<RequestResponseLoggingMiddleware>();

        app.UseCors("CorsPolicy");

        app.UseAuthentication();

        app.UseHealthChecks("/healthz", new HealthCheckOptions
        {
            Predicate = _ => true,
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        });

        app.UseHealthChecksUI();

        var option = new RewriteOptions();
        option.AddRedirect("^$", "healthchecks-ui");
        app.UseRewriter(option);

        await app.UseOcelot();
    }
}