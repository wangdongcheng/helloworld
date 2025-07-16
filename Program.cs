using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Data.SqlClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

class Program
{
    /// <summary>
    /// 从指定路径的JSON文件中，根据key返回对应的value。
    /// </summary>
    /// <param name="jsonFilePath">JSON文件路径</param>
    /// <param name="key">要查找的key</param>
    /// <returns>对应的value字符串，找不到返回null</returns>
    public static Database? GetJsonValue(string jsonFilePath, string key)
    {
        if (string.IsNullOrEmpty(jsonFilePath) || string.IsNullOrEmpty(key))
            return null;
        try
        {
            var jsonText = System.IO.File.ReadAllText(jsonFilePath);
            var jObj = JObject.Parse(jsonText);
            // 支持嵌套key（如 a.b.c）
            var token = jObj.SelectToken(key);
            return token?.ToObject<Database>();
            // return token?.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GetJsonValue error: {ex.Message}");
            return null;
        }
    }

    static void Main()
    {

        // var databaseObj = new Database();
        var databaseObj = GetJsonValue("..\\..\\..\\config.json", "database");
        string searchValue = "MAIN DIV"; // value to search for
        // string Database = databaseObj.Name; // database name
                                   // Connection string to the SQL Server database
        Console.WriteLine("Please enter the database password:");                                   
        string Password = Console.ReadLine();
        string connectionString = $"Server={databaseObj.Server};Database={databaseObj.Name};User Id={databaseObj.Id};Password={Password};";
        string tableName = "";       // table name
        var tablenames = new List<string>();
        // tablenames.Add("SL_ACC_TEMP");
        tablenames.Add("STK_STOCK");
        tablenames.Add("STK_STOCK_2");
        tablenames.Add("STK_STOCK3");
        tablenames.Add("STK_STOCK4");

        var matchingColumns = FindMatchingColumns(connectionString, tableName, searchValue, tablenames);

        Console.WriteLine($"the columns that contain the value {searchValue} in table {tableName}:");
        foreach (var col in matchingColumns)
        {
            Console.WriteLine(col);
        }
    }

    static List<string> FindMatchingColumns(string connStr, string tableName, string searchValue, List<string> tablenames)
    {
        var matchingColumns = new List<string>();
        using (var conn = new SqlConnection(connStr))
        {
            conn.Open();
            foreach (string tab in tablenames)
            {
                tableName = tab; // Set the current table name
                // get column names
                string getColumnsCmd = @"
                SELECT COLUMN_NAME 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = @TableName";

                var columnNames = new List<string>();

                using (var cmd = new SqlCommand(getColumnsCmd, conn))
                {
                    cmd.Parameters.AddWithValue("@TableName", tableName);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            columnNames.Add(reader.GetString(0));
                        }
                    }
                }

                // select columns with LIKE condition
                var likeConditions = new List<string>();
                foreach (var col in columnNames)
                {
                    if (col.Contains("DATE"))  // Check if the column name contains "date"
                    {
                        likeConditions.Add($"CONVERT(VARCHAR, [{col}], 23) LIKE @SearchPattern");
                    }
                    likeConditions.Add($"[{col}] LIKE @SearchPattern");
                }

                string whereClause = string.Join(" OR ", likeConditions);
                string searchSql = $"SELECT TOP 10 * FROM [{tableName}] WHERE {whereClause}";

                using (var cmd = new SqlCommand(searchSql, conn))
                {
                    cmd.Parameters.AddWithValue("@SearchPattern", $"{searchValue}");

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            foreach (var col in columnNames)
                            {
                                var val = reader[col];
                                if (val != DBNull.Value && val.ToString().Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                                {
                                    matchingColumns.Add($"{tableName}:{col}");
                                }
                                else if (col.Contains("DATE") && DateTime.TryParse(val.ToString(), out DateTime dateValue))
                                {
                                    // Check if the date matches the search value
                                    if (dateValue.ToString("yyyy-MM-dd").Contains(searchValue))
                                    {
                                        matchingColumns.Add($"{tableName}:{col}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            conn.Close();
            return matchingColumns;
        }
    }
}