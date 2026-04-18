using Windows.ApplicationModel.Resources;

namespace FluentTaskScheduler.Services
{
    public static class LocalizationService
    {
        private static readonly ResourceLoader Loader = ResourceLoader.GetForViewIndependentUse();

        public static string GetString(string key, string fallback = "")
        {
            try
            {
                var value = Loader.GetString(key);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
            catch
            {
            }

            return string.IsNullOrEmpty(fallback) ? key : fallback;
        }
    }
}
