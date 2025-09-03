using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.IO;

public class SteamMarketPrice
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("lowest_price")]
    public string? LowestPrice { get; set; }

    [JsonPropertyName("volume")]
    public string? Volume { get; set; }

    [JsonPropertyName("median_price")]
    public string? MedianPrice { get; set; }
}

public class AppConfig
{
    public int ActivePreset { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int CurrencyType { get; set; }
    public string NtfyTopic { get; set; } = string.Empty;
    public float PriceRiseThreshold { get; set; }
    public float PriceDropThreshold { get; set; }
}

public class Program
{
    private static readonly HttpClient client = new HttpClient();

    private static int currencyType = 1; // Default currency = USD
    private static string apiUrl => $"https://steamcommunity.com/market/priceoverview/?currency={currencyType}&appid=730&market_hash_name={itemName}";

    private static int activePreset = 1;
    private static float priceRiseThreshold = 0.0f;
    private static float priceDropThreshold = 0.0f;
    private static string ntfyTopic = string.Empty;
    private static bool isNtfyEnabled => !string.IsNullOrEmpty(ntfyTopic);
    private static string itemName = string.Empty;

    private static float? previousLowestPrice = null;
    private static float? previousMedianPrice = null;
    private static bool isAlertSent = false;

    private static readonly string configFile = Path.Combine(AppContext.BaseDirectory, "config.json");


    public static async Task Main(string[] args)
    {
        // Load configuration from file
        var config = LoadConfig();
        if (config != null)
            while (true)
            {
                Console.Clear();

                itemName = config.ItemName;
                currencyType = config.CurrencyType;
                ntfyTopic = config.NtfyTopic;
                priceRiseThreshold = config.PriceRiseThreshold;
                priceDropThreshold = config.PriceDropThreshold;

                for (int i = 1; i <= 5; i++)
                {
                    if (activePreset == i)
                    {
                        Console.BackgroundColor = ConsoleColor.Cyan;
                        Console.ForegroundColor = ConsoleColor.Black;
                    }
                    Console.Write($" Preset {i} ");
                    Console.ResetColor();

                    if (i < 5)
                    {
                        Console.Write("|");
                    }
                }

                Console.WriteLine("\n\n--- Selected configuration ---");
                Console.WriteLine($"Tracked Item: {itemName.Replace("%20", " ")}");
                Console.WriteLine($"Currency Type: {currencyType}");
                Console.WriteLine($"Ntfy Topic: {ntfyTopic}");
                Console.WriteLine($"Price Rise Threshold: {priceRiseThreshold}");
                Console.WriteLine($"Price Drop Threshold: {priceDropThreshold}");
                Console.WriteLine("-----------------------------------");

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\nUse Left/Right arrow keys [or A/D] to switch presets.");
                Console.WriteLine("Press C to change selected preset's configuration");
                Console.WriteLine("Press Enter to start tracking.");
                Console.ResetColor();

                var keyInfo = Console.ReadKey(true);

                if (keyInfo.Key == ConsoleKey.LeftArrow || keyInfo.Key == ConsoleKey.A)
                {
                    activePreset = (activePreset == 1) ? 5 : activePreset - 1;
                }
                else if (keyInfo.Key == ConsoleKey.RightArrow || keyInfo.Key == ConsoleKey.D)
                {
                    activePreset = (activePreset == 5) ? 1 : activePreset + 1;
                }
                else if (keyInfo.Key == ConsoleKey.C)
                {
                    // Enable configuration mode


                    Console.Clear();
                    Console.WriteLine($"--- Configuring Preset {activePreset} ---");

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Use Up/Down arrow keys [or W/S] to select an option");
                }
                else if (keyInfo.Key == ConsoleKey.Enter)
                {
                    // Save the last selected preset before starting
                    config.ActivePreset = activePreset;
                    SaveConfig(config);
                    break;
                }
            }
        else
        {
            Console.WriteLine("--- Welcome to Steam Market Notifier! ---");
            while (true)
            {
                Console.WriteLine("Write the exact english name of the item you want to track (case-sensitive):");
                string? itemInput = Console.ReadLine();

                if (!string.IsNullOrEmpty(itemInput))
                {
                    itemName = itemInput.Replace(" ", "%20");
                    break;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Item name cannot be empty. Please try again.");
                    Console.ResetColor();
                }
            }

            Console.WriteLine("--- Choose your preffered currency ---");
            while (true)
            {
                Console.WriteLine("Input currency type (1-10): ");
                Console.WriteLine("1. USD (default)");
                Console.WriteLine("2. GBP");
                Console.WriteLine("3. EUR");
                Console.WriteLine("4. CHF");
                Console.WriteLine("5. RUB");
                Console.WriteLine("6. PLN");
                Console.WriteLine("7. BRL");
                Console.WriteLine("8. JPY");
                Console.WriteLine("9. NOK");
                Console.WriteLine("10. AED");

                string? currencyInput = Console.ReadLine();

                if (int.TryParse(currencyInput, out currencyType) && currencyType >= 1 && currencyType <= 10)
                {
                    break;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Wrong input. Input a number between 1 to 10.");
                    Console.ResetColor();
                }
            }

            Console.WriteLine("--- Set your Ntfy token ID for notifications ---");
            while (true)
            {
                Console.WriteLine("If you don't have one, you can create it at https://ntfy.sh/");
                Console.WriteLine("Or leave it empty and press enter to skip notifications.");

                string? ntfyInput = Console.ReadLine();

                if (string.IsNullOrEmpty(ntfyInput))
                {
                    ntfyTopic = string.Empty; // No notifications
                    break;
                }
                else if (ntfyInput.Length >= 5 && ntfyInput.Length <= 64)
                {
                    ntfyTopic = ntfyInput.Trim();
                    break;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Wrong input. Input a valid token ID (5-64 characters).");
                    Console.ResetColor();
                }
            }

            if (isNtfyEnabled)
            {
                Console.WriteLine("--- Configure Price Alert ---");
                while (true)
                {
                    Console.Write("Enter price rise threshold (ex. 08,50) or press enter to skip: ");
                    string? priceRiseInput = Console.ReadLine();

                    // Convert comma to dot for decimal parsing
                    if (priceRiseInput != null)
                    {
                        priceRiseInput = priceRiseInput.Replace(',', '.');
                    }

                    if (float.TryParse(priceRiseInput, NumberStyles.Any, CultureInfo.InvariantCulture, out priceRiseThreshold) && priceRiseThreshold > 0)
                    {
                        break;
                    }
                    else if (string.IsNullOrEmpty(priceRiseInput))
                    {
                        priceRiseThreshold = 0.0f; // No threshold set
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Wrong input. Enter positive number.");
                        Console.ResetColor();
                    }

                    Console.Write("Enter price drop threshold (ex. 05,23) or press enter to skip: ");
                    string? priceDropInput = Console.ReadLine();

                    if (priceDropInput != null)
                    {
                        priceDropInput = priceDropInput.Replace(',', '.');
                    }

                    if (float.TryParse(priceDropInput, NumberStyles.Any, CultureInfo.InvariantCulture, out priceDropThreshold) && priceDropThreshold > 0)
                    {
                        break;
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Wrong input. Enter positive number.");
                        Console.ResetColor();
                    }
                }
            }

            // Save configuration to file
            config = new AppConfig
            {
                ActivePreset = activePreset,
                ItemName = Uri.UnescapeDataString(itemName),
                CurrencyType = currencyType,
                PriceRiseThreshold = priceRiseThreshold,
                PriceDropThreshold = priceDropThreshold,
                NtfyTopic = ntfyTopic
            };
            SaveConfig(config);

            Console.WriteLine("Tracking Steam Market price of selected item...");
        }

        while (true)
        {
            await FetchAndDisplayPrice();
            Console.WriteLine($"\nNext update in 5 minutes...");
            await Task.Delay(TimeSpan.FromMinutes(5));
        }
    }

    // === Fetch price data from Steam Market API and display it ===
    private static async Task FetchAndDisplayPrice()
    {
        Console.Clear();
        Console.WriteLine($"--- Last update: {DateTime.Now:HH:mm:ss} ---");
        Console.WriteLine($"Tracking item: {itemName.Replace("%20", " ")}");
        Console.WriteLine($"{apiUrl}");

        try
        {
            string responseBody = await client.GetStringAsync(apiUrl);
            var priceData = JsonSerializer.Deserialize<SteamMarketPrice>(responseBody);

            if (priceData?.Success == true && priceData.LowestPrice != null && priceData.MedianPrice != null)
            {
                float currentLowest = ParsePrice(priceData.LowestPrice);
                float currentMedian = ParsePrice(priceData.MedianPrice);

                Console.WriteLine("------------------------------------------");
                PrintPriceWithColor("Lowest price: \t\t", priceData.LowestPrice, currentLowest, previousLowestPrice);
                PrintPriceWithColor("Median price: \t\t", priceData.MedianPrice, currentMedian, previousMedianPrice);
                Console.WriteLine($"Volume (24h): \t\t{priceData.Volume}");
                Console.WriteLine("------------------------------------------");
                Console.WriteLine($"Lowest price change: \t{(previousLowestPrice.HasValue ? (currentLowest - previousLowestPrice.Value).ToString("F2", CultureInfo.InvariantCulture) : "N/A")}");
                Console.WriteLine($"Median price change: \t{(previousMedianPrice.HasValue ? (currentMedian - previousMedianPrice.Value).ToString("F2", CultureInfo.InvariantCulture) : "N/A")}");
                Console.WriteLine("------------------------------------------");

                if (currentLowest > priceRiseThreshold && isNtfyEnabled && priceRiseThreshold != 0.0f && !isAlertSent)
                {
                    Console.WriteLine("\nWent over the threshold! Sending notification...");
                    string message = $"Item's price went over the threshold! Current price: {priceData.LowestPrice}";
                    await SendNtfyNotification(message);
                    isAlertSent = true; // Set alert as sent
                }
                // Reset alert if price is not hitting either threshold
                else if (currentLowest <= priceRiseThreshold && currentLowest >= priceDropThreshold && isAlertSent)
                {
                    Console.WriteLine("\nPrice not hitting the threshold anymore. Alert reset.");
                    isAlertSent = false;
                }

                if (currentLowest < priceDropThreshold && isNtfyEnabled && priceDropThreshold != 0.0f && !isAlertSent)
                {
                    Console.WriteLine("\nPrice is below the drop threshold. Sending notification...");
                    string message = $"Item's price is below the price drop threshold! Current price: {priceData.LowestPrice}";
                    await SendNtfyNotification(message);
                    isAlertSent = true;
                }

                // Update previous prices for next comparison
                previousLowestPrice = currentLowest;
                previousMedianPrice = currentMedian;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("API did respond with a message, but with incorrect data. Check if item's name is set correctly");
                Console.ResetColor();
            }
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"\nAn error occured: {e.Message}");
            Console.ResetColor();
        }
    }

    // === Send notification using ntfy.sh service ===
    private static async Task SendNtfyNotification(string message)
    {
        try
        {
            var content = new StringContent(message, Encoding.UTF8, "text/plain");
            var request = new HttpRequestMessage(HttpMethod.Post, $"https://ntfy.sh/{ntfyTopic}")
            {
                Content = content
            };
            request.Headers.Add("Title", "Steam Market Notifier");
            request.Headers.Add("Priority", "high");
            request.Headers.Add("Tags", "tada");

            var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("Notification sent successfuly!");
            }
            else
            {
                Console.WriteLine($"Error while sending the notification: {response.StatusCode}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Critical error while sending the notification: {e.Message}");
        }
    }

    // === Supporting function to parse price strings ===
    private static float ParsePrice(string priceString)
    {
        string cleanPrice = System.Text.RegularExpressions.Regex.Replace(priceString, @"[^\d,.]", "").Replace(",", ".");
        if (float.TryParse(cleanPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out float result))
        {
            return result;
        }
        return 0;
    }

    // === Supporting function to print prices with color based on change ===
    private static void PrintPriceWithColor(string label, string priceString, float currentPrice, float? previousPrice)
    {
        Console.Write(label);

        if (previousPrice.HasValue)
        {
            if (currentPrice > previousPrice.Value)
            {
                Console.ForegroundColor = ConsoleColor.Green;
            }
            else if (currentPrice < previousPrice.Value)
            {
                Console.ForegroundColor = ConsoleColor.Red;
            }
        }

        // If previousPrice is null, just print the price without color
        Console.WriteLine(priceString);
        Console.ResetColor();
    }

    private static AppConfig? LoadConfig()
    {
        if (File.Exists(configFile))
        {
            try
            {
                string json = File.ReadAllText(configFile);
                return JsonSerializer.Deserialize<AppConfig>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading config file: {ex.Message}");
                return null;
            }
        }
        return null;
    }

    private static void SaveConfig(AppConfig config)
    {
        try
        {
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving config file: {ex.Message}");
        }
    }
}