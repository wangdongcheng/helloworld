using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

class Program
{
    static void Main()
    {
        string searchValue = "30AAV001"; // value to search for
        string Database = "VJSCLTest"; // database name
        // Connection string to the SQL Server database
        string connectionString = $"Server=VJSERVER03;Database={Database};User Id=sa;Password=net1032;";
        string tableName = "";       // table name
        ArrayList tablenames = new ArrayList();
        // tablenames.Add("SL_ACC_TEMP");
        // tablenames.Add("SL_ACCESS_PAYMENTS");
        // tablenames.Add("SL_ACCESS_PAYMENTS_ERRORS");
        tablenames.Add("SL_ACCOUNTS");
        tablenames.Add("SL_ACCOUNTS2");
        // tablenames.Add("SL_ACCOUNTS2_12012024");
        tablenames.Add("SL_ACCOUNTS3");
        tablenames.Add("SL_ADDRESSES");
        tablenames.Add("SL_ADDRESSES2");
        tablenames.Add("SL_ADDRESSES3");
        // tablenames.Add("SL_ALLOC_HISTORY");
        // tablenames.Add("SL_ALLOCATION_TEMP");
        // tablenames.Add("SL_ANALYSIS");
        // tablenames.Add("SL_ANALYSIS2");
        // tablenames.Add("SL_AP_BANK_ANALYSES");
        // tablenames.Add("SL_BACS_FORMATS");
        // tablenames.Add("SL_BUYING_GROUP_MEMBERS");
        // tablenames.Add("SL_BUYING_GROUPS");
        // tablenames.Add("SL_CAMPAIGN_ASSIGN");
        // tablenames.Add("SL_DD_SIGNUPS");
        // tablenames.Add("SL_DD_SUGGESTED_COLLECTION_HISTORY");
        // tablenames.Add("SL_DD_SUGGESTED_COLLECTION_TRANS");
        // tablenames.Add("SL_DD_SUGGESTED_COLLECTIONS");
        // tablenames.Add("SL_DETAIL_PLUGINS_DATETIME");
        // tablenames.Add("SL_DETAIL_PLUGINS_VCHAR");
        // tablenames.Add("SL_DIRECT_DEBIT_STATUSES");
        // tablenames.Add("SL_LETTERS");
        // tablenames.Add("SL_NOTES");
        // tablenames.Add("SL_NOTES_LINK");
        // tablenames.Add("SL_PL_ALLOC_CORRECT_TEMP");
        // tablenames.Add("SL_PL_NL_DETAIL");
        // tablenames.Add("SL_PL_NL_DETAIL_ACCRUALS");
        // tablenames.Add("SL_PL_NL_DETAIL2");
        // tablenames.Add("SL_PL_STATEMENTS_TEMP");
        // tablenames.Add("SL_PROSPECT_LINK");
        // tablenames.Add("SL_TERMS");
        // tablenames.Add("SL_TRANSACTION_DD_COLLECTION_HISTORY");
        // tablenames.Add("SL_TRANSACTION_NOTES");
        // tablenames.Add("SL_TRANSACTIONS");
        // tablenames.Add("SL_TRANSACTIONS2");
        // tablenames.Add("SL_TRN_LOCKS");
        // tablenames.Add("SL_TRN_TEMP");

        
        var matchingColumns = FindMatchingColumns(connectionString, tableName, searchValue, tablenames);

        Console.WriteLine($"the columns that contain the value {searchValue} in table {tableName}:");
        foreach (var col in matchingColumns)
        {
            Console.WriteLine(col);
        }
    }

    static List<string> FindMatchingColumns(string connStr, string tableName, string searchValue, ArrayList tablenames)
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