$ErrorActionPreference = 'Stop'
$npgsql = (Resolve-Path 'src/Odca.Bootstrap/bin/Release/net10.0/Npgsql.dll').Path
[System.Reflection.Assembly]::LoadFrom($npgsql) | Out-Null
$conn = [Npgsql.NpgsqlConnection]::new('Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=123456')
$conn.Open()
try {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT 1 FROM pg_database WHERE datname = 'odca_test_disposable'"
    $exists = $cmd.ExecuteScalar()
    if (-not $exists) {
        $create = $conn.CreateCommand()
        $create.CommandText = "CREATE DATABASE odca_test_disposable"
        $create.ExecuteNonQuery()
        Write-Host "Database odca_test_disposable created successfully."
    } else {
        Write-Host "Database odca_test_disposable already exists."
    }
} finally {
    $conn.Close()
}
