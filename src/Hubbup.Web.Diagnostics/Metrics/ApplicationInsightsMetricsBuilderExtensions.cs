using Microsoft.Extensions.DependencyInjection;

namespace Hubbup.Web.Diagnostics.Metrics
{
    public static class ApplicationInsightsMetricsBuilderExtensions
    {
        public static void AddApplicationInsights(this IMetricsBuilder metricsBuilder)
        {
            metricsBuilder.Services.AddSingleton<IMetricsSink, ApplicationInsightsMetricsSink>();
        }
    }
}
