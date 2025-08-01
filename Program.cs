using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

// Configuration models
public class Database
{
    public string Server { get; set; }
    public string Name { get; set; }
    public string Id { get; set; }
}

public class SearchResult
{
    public string TableName { get; set; }
    public string ColumnName { get; set; }
    public object Value { get; set; }
    
    public override string ToString() => $"{TableName}:{ColumnName} = {Value}";
}

class Program
{
    private static readonly string ConfigFileName = "config.json";
    
    public static async Task Main()
    {
        try
        {
            var configPath = GetConfigPath();
            var databaseConfig = GetConfiguration<Database>(configPath, "database");
            var tableNames = GetConfiguration<List<string>>(configPath, "searchtables");
            
            if (databaseConfig == null || tableNames == null || !tableNames.Any())
            {
                Console.WriteLine("Failed to load configuration. Please check config.json file.");
                return;
            }

            var connectionString = await GetConnectionStringAsync(databaseConfig);
            if (string.IsNullOrEmpty(connectionString))
                return;

            await RunSearchLoopAsync(connectionString, tableNames);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Application error: {ex.Message}");
        }
    }

    private static string GetConfigPath()
    {
        var currentDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
        var configPath = Path.Combine(currentDirectory ?? "", "..", "..", "..", ConfigFileName);
        return Path.GetFullPath(configPath);
    }

    private static T GetConfiguration<T>(string jsonFilePath, string key) where T : class
    {
        if (string.IsNullOrEmpty(jsonFilePath) || string.IsNullOrEmpty(key))
            return null;

        try
        {
            if (!File.Exists(jsonFilePath))
            {
                Console.WriteLine($"Configuration file not found: {jsonFilePath}");
                return null;
            }

            var jsonText = File.ReadAllText(jsonFilePath);
            var jObj = JObject.Parse(jsonText);
            var token = jObj.SelectToken(key);
            return token?.ToObject<T>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading configuration '{key}': {ex.Message}");
            return null;
        }
    }

    private static async Task<string> GetConnectionStringAsync(Database databaseConfig)
    {
        Console.WriteLine("Please enter the last 4 digits of database password:");
        var password = Console.ReadLine();
        
        if (string.IsNullOrWhiteSpace(password))
        {
            Console.WriteLine("Password cannot be empty.");
            return null;
        }

        var connectionString = $"Server={databaseConfig.Server};Database={databaseConfig.Name};" +
                              $"User Id={databaseConfig.Id};Password=net{password};" +
                              $"Connection Timeout=30;";

        // Test connection
        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            Console.WriteLine("Database connection successful!");
            return connectionString;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to connect to database: {ex.Message}");
            return null;
        }
    }

    private static async Task RunSearchLoopAsync(string connectionString, List<string> tableNames)
    {
        Console.WriteLine("Please input the value to search for (type '!exit' or press ctrl+c to quit):");
        
        string searchValue;
        while (!string.IsNullOrEmpty(searchValue = Console.ReadLine()) && searchValue != "!exit")
        {
            try
            {
                var results = await FindMatchingColumnsAsync(connectionString, searchValue, tableNames);
                
                if (results.Any())
                {
                    Console.WriteLine($"\nFound {results.Count} matches for '{searchValue}':");
                    foreach (var result in results.Take(50)) // Limit output
                    {
                        Console.WriteLine($"  {result}");
                    }
                    
                    if (results.Count > 50)
                        Console.WriteLine($"  ... and {results.Count - 50} more results (showing first 50)");
                }
                else
                {
                    Console.WriteLine($"No matches found for '{searchValue}'");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Search error: {ex.Message}");
            }
            
            Console.WriteLine("\nPlease input the value to search for (type '!exit' or press ctrl+c to quit):");
        }
        
        Console.WriteLine("Exiting program...");
    }

    private static async Task<List<SearchResult>> FindMatchingColumnsAsync(
        string connectionString, 
        string searchValue, 
        List<string> tableNames)
    {
        var results = new List<SearchResult>();
        
        // Process tables sequentially to avoid connection conflicts
        foreach (var tableName in tableNames)
        {
            var tableResults = await SearchTableAsync(connectionString, tableName, searchValue);
            results.AddRange(tableResults);
        }

        return results.Distinct().ToList();
    }

    private static async Task<List<SearchResult>> SearchTableAsync(
        string connectionString, 
        string tableName, 
        string searchValue)
    {
        var results = new List<SearchResult>();
        
        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            
            // Get column information with data types
            var columns = await GetTableColumnsAsync(connection, tableName);
            if (!columns.Any())
                return results;

            // Build dynamic search query
            var searchConditions = BuildSearchConditions(columns, searchValue);
            if (!searchConditions.Any())
                return results;

            var query = $@"
                SELECT TOP 10 {string.Join(", ", columns.Select(c => $"[{c.Name}]"))}
                FROM [{tableName}] 
                WHERE {string.Join(" OR ", searchConditions)}";

            using var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@SearchValue", searchValue);
            command.Parameters.AddWithValue("@SearchPattern", $"%{searchValue}%");
            command.CommandTimeout = 60; // Set command timeout here

            using var reader = await command.ExecuteReaderAsync();
            
            if (await reader.ReadAsync())
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var columnName = reader.GetName(i);
                    var value = reader.GetValue(i);
                    
                    if (value != DBNull.Value && ValueContainsSearch(value, searchValue, columnName))
                    {
                        results.Add(new SearchResult
                        {
                            TableName = tableName,
                            ColumnName = columnName,
                            Value = value
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error searching table {tableName}: {ex.Message}");
        }

        return results;
    }

    private static async Task<List<(string Name, string DataType)>> GetTableColumnsAsync(
        SqlConnection connection, 
        string tableName)
    {
        var columns = new List<(string Name, string DataType)>();
        
        const string query = @"
            SELECT COLUMN_NAME, DATA_TYPE 
            FROM INFORMATION_SCHEMA.COLUMNS 
            WHERE TABLE_NAME = @TableName
            ORDER BY ORDINAL_POSITION";

        using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@TableName", tableName);
        command.CommandTimeout = 30; // Set command timeout
        
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add((
                reader.GetString(0), // COLUMN_NAME
                reader.GetString(1)  // DATA_TYPE
            ));
        }

        return columns;
    }

    private static List<string> BuildSearchConditions(
        List<(string Name, string DataType)> columns, 
        string searchValue)
    {
        var conditions = new List<string>();
        
        foreach (var (name, dataType) in columns)
        {
            switch (dataType.ToLower())
            {
                case "varchar":
                case "nvarchar":
                case "char":
                case "nchar":
                case "text":
                case "ntext":
                    conditions.Add($"[{name}] LIKE @SearchPattern");
                    break;
                    
                case "date":
                case "datetime":
                case "datetime2":
                case "smalldatetime":
                    if (DateTime.TryParse(searchValue, out _) || 
                        searchValue.Length >= 4) // Year search
                    {
                        conditions.Add($"CONVERT(VARCHAR, [{name}], 23) LIKE @SearchPattern");
                    }
                    break;
                    
                case "int":
                case "bigint":
                case "smallint":
                case "tinyint":
                    if (int.TryParse(searchValue, out _))
                    {
                        conditions.Add($"[{name}] = @SearchValue");
                    }
                    break;
                    
                case "decimal":
                case "numeric":
                case "float":
                case "real":
                    if (decimal.TryParse(searchValue, out _))
                    {
                        conditions.Add($"[{name}] = @SearchValue");
                    }
                    break;
            }
        }
        
        return conditions;
    }

    private static bool ValueContainsSearch(object value, string searchValue, string columnName)
    {
        if (value == null || value == DBNull.Value)
            return false;

        var stringValue = value.ToString();
        
        // Handle date columns specially
        if (columnName.ToUpper().Contains("DATE") && value is DateTime dateValue)
        {
            return dateValue.ToString("yyyy-MM-dd").Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                   dateValue.ToString("MM/dd/yyyy").Contains(searchValue, StringComparison.OrdinalIgnoreCase);
        }
        
        return stringValue.Contains(searchValue, StringComparison.OrdinalIgnoreCase);
    }
}