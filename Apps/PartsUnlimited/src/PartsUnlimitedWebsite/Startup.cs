// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PartsUnlimited.Areas.Admin;
using PartsUnlimited.Models;
using PartsUnlimited.Queries;
using PartsUnlimited.Recommendations;
using PartsUnlimited.Search;
using PartsUnlimited.Security;
using PartsUnlimited.Telemetry;
using PartsUnlimited.WebsiteConfiguration;
using System;

namespace PartsUnlimited
{
    public class Startup
    {
        public IConfiguration Configuration { get; }
        public IServiceCollection service { get; private set; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            this.service = services;
            //If this type is present - we're on mono
            var runningOnMono = Type.GetType("Mono.Runtime") != null;
            var sqlConnectionString = Configuration[ConfigurationPath.Combine("ConnectionStrings", "DefaultConnectionString")];
            var useInMemoryDatabase = string.IsNullOrWhiteSpace(sqlConnectionString);

            if (useInMemoryDatabase || runningOnMono)
            {
                sqlConnectionString = "";
            }

            SetupDependencyInjection(services, sqlConnectionString);
            SetupRecommendationService(services);

            // Add EF services to the services container
            services.AddDbContext<PartsUnlimitedContext>();


            // Add Identity services to the services container
            services.AddIdentity<ApplicationUser, IdentityRole>()
                .AddEntityFrameworkStores<PartsUnlimitedContext>()
                .AddDefaultTokenProviders();

            // Configure admin policies
            services.AddAuthorization(auth =>
            {
                auth.AddPolicy(AdminConstants.Role,
                    authBuilder =>
                    {
                        authBuilder.RequireClaim(AdminConstants.ManageStore.Name, AdminConstants.ManageStore.Allowed);
                    });

            });

            services.AddDatabaseDeveloperPageExceptionFilter();


            // services.AddApplicationInsightsTelemetry(Configuration);

            

            // We need access to these settings in a static extension method, so DI does not help us :(
            ContentDeliveryNetworkExtensions.Configuration = new ContentDeliveryNetworkConfiguration(Configuration.GetSection("CDN"));

            // Add MVC services to the services container
            services.AddControllersWithViews();
            services.AddRazorPages();

            

            // Add session related services.
            //services.AddCaching();
            services.AddSession();
        }

        private void SetupDependencyInjection(IServiceCollection services, string sqlConnectionString)
        {
            // Add implementations
            services.AddSingleton<IMemoryCache, MemoryCache>();
            services.AddScoped<IOrdersQuery, OrdersQuery>();
            services.AddScoped<IRaincheckQuery, RaincheckQuery>();

            services.AddSingleton<ITelemetryProvider, EmptyTelemetryProvider>();
            services.AddScoped<IProductSearch, StringContainsProductSearch>();

            services.AddScoped<IWebsiteOptions>(p =>
            {
                var telemetry = p.GetRequiredService<ITelemetryProvider>();

                return new ConfigurationWebsiteOptions(Configuration.GetSection("WebsiteOptions"), telemetry);
            });

            services.AddScoped<IApplicationInsightsSettings>(p =>
            {
                return new ConfigurationApplicationInsightsSettings(Configuration.GetSection(ConfigurationPath.Combine("Keys", "ApplicationInsights")));
            });

            // Associate IPartsUnlimitedContext and PartsUnlimitedContext with context
            services.AddTransient<IPartsUnlimitedContext>(x => new PartsUnlimitedContext(sqlConnectionString));
            services.AddTransient(x => new PartsUnlimitedContext(sqlConnectionString));

            //Add InMemoryCache
            services.AddSingleton<IMemoryCache, MemoryCache>();
        }

        private void SetupRecommendationService(IServiceCollection services)
        {
            var azureMlConfig = new AzureMLFrequentlyBoughtTogetherConfig(Configuration.GetSection(ConfigurationPath.Combine("Keys", "AzureMLFrequentlyBoughtTogether")));

            // If keys are not available for Azure ML recommendation service, register an empty recommendation engine
            if (string.IsNullOrEmpty(azureMlConfig.AccountKey) || string.IsNullOrEmpty(azureMlConfig.ModelName))
            {
                services.AddSingleton<IRecommendationEngine, EmptyRecommendationsEngine>();
            }
            else
            {
                services.AddSingleton<IAzureMLAuthenticatedHttpClient, AzureMLAuthenticatedHttpClient>();
                services.AddSingleton<IAzureMLFrequentlyBoughtTogetherConfig>(azureMlConfig);
                services.AddScoped<IRecommendationEngine, AzureMLFrequentlyBoughtTogetherRecommendationEngine>();
            }
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseMigrationsEndPoint();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            // Configure Session.
            app.UseSession();

            // Add static files to the request pipeline
            app.UseStaticFiles();      

            AppBuilderLoginProviderExtensions.AddLoginProviders(this.service, new ConfigurationLoginProviders(Configuration.GetSection("Authentication")));
            // Add login providers (Microsoft/AzureAD/Google/etc).  This must be done after `app.UseIdentity()`
            //app.AddLoginProviders( new ConfigurationLoginProviders(Configuration.GetSection("Authentication")));

            app.UseRouting();

            // Add cookie-based authentication to the request pipeline
            app.UseAuthentication();
            app.UseAuthorization();

            // Add MVC to the request pipeline
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "areaRoute",
                    pattern: "{area:exists}/{controller}/{action=Index}");

                endpoints.MapControllerRoute(
                   name: "default",
                   pattern: "{controller=Home}/{action=Index}/{id?}");
 
                endpoints.MapControllerRoute(
                   name: "api",
                   pattern: "{controller}/{id?}");

                endpoints.MapRazorPages();
            });
        }
    }
}