using System;
using System.Threading.Tasks;

// Copyright © Charlie Howard 2025-2026 All rights reserved

namespace Cardmarket_Price_Updater.Core
{
    public static class RetryPolicy
    {
        public static async Task<T> RunAsync<T>(
            Func<Task<T>> action,
            int maxAttempts,
            double initialDelaySeconds,
            Action<string>? log = null,
            string? label = null)
        {
            if (maxAttempts < 1) maxAttempts = 1;
            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    return await action();
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (attempt == maxAttempts)
                        break;

                    double delay = initialDelaySeconds * Math.Pow(2, attempt - 1);
                    log?.Invoke(
                        $"{(label ?? "Request")} failed (attempt {attempt}/{maxAttempts}): {ex.Message}. " +
                        $"Retrying in {delay:0.#}s...");
                    await Task.Delay(TimeSpan.FromSeconds(delay));
                }
            }

            throw new AggregateException(
                $"{(label ?? "Request")} failed after {maxAttempts} attempts.", lastException!);
        }
    }
}
