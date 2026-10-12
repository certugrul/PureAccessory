using System.IO;
using System.Text.Json;
using ERP.Desktop.Models;
using Microsoft.Data.SqlClient;

namespace ERP.Desktop.Services;

/// <summary>
/// Tüm verileri MS SQL Server (localhost / ERPSystem) ile senkronize eder.
/// JSON dosyalarındaki mevcut verileri tek seferlik SQL Server tablolarına aktarır
/// ve tabloların eksiksiz var olmasını garanti eder.
/// </summary>
public class DatabaseSyncService
{
    private readonly DatabaseService _dbService;
    private static readonly string StorageDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
    private static bool _initialized = false;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    public DatabaseSyncService(DatabaseService? dbService = null)
    {
        _dbService = dbService ?? new DatabaseService();
    }

    public static async Task InitializeAndMigrateAsync()
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            var service = new DatabaseSyncService(new DatabaseService(DatabaseService.DefaultSqlServerConnectionString));
            bool connected = await service._dbService.TestConnectionAsync();
            if (!connected)
            {
                System.Diagnostics.Debug.WriteLine("[DatabaseSyncService] SQL Server bağlantısı kurulamadı. Yerel JSON kullanılacak.");
                return;
            }

            await service.EnsureTablesCreatedAsync();
            await service.MigrateAllJsonDataAsync();
            _initialized = true;
            System.Diagnostics.Debug.WriteLine("[DatabaseSyncService] SQL Server senkronizasyonu başarıyla tamamlandı.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DatabaseSyncService] Senkronizasyon hatası: {ex.Message}");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task EnsureTablesCreatedAsync()
    {
        string connStr = _dbService.ProviderType == DatabaseProviderType.SqlServer 
            ? _dbService.ConnectionString 
            : DatabaseService.DefaultSqlServerConnectionString;
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var ddl = @"
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Users')
BEGIN
    CREATE TABLE Users (
        Id INT PRIMARY KEY IDENTITY(1,1),
        Username NVARCHAR(100) NOT NULL UNIQUE,
        FullName NVARCHAR(150) NOT NULL,
        Role NVARCHAR(50) NOT NULL,
        PinCode NVARCHAR(50) NOT NULL,
        IsActive BIT NOT NULL DEFAULT 1,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        LastLoginAt DATETIME2 NULL,
        PermissionsJson NVARCHAR(MAX) NULL
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Warehouses')
BEGIN
    CREATE TABLE Warehouses (
        Id INT PRIMARY KEY IDENTITY(1,1),
        Code NVARCHAR(50) NOT NULL,
        Name NVARCHAR(100) NOT NULL,
        Location NVARCHAR(200) NULL,
        Description NVARCHAR(255) NULL,
        IsDefault BIT NOT NULL DEFAULT 0,
        IsActive BIT NOT NULL DEFAULT 1,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WarehouseStocks')
BEGIN
    CREATE TABLE WarehouseStocks (
        Id INT PRIMARY KEY IDENTITY(1,1),
        WarehouseId INT NOT NULL,
        ProductId INT NOT NULL,
        Quantity DECIMAL(18,2) NOT NULL DEFAULT 0,
        MinStockLevel DECIMAL(18,2) NOT NULL DEFAULT 0,
        MaxStockLevel DECIMAL(18,2) NOT NULL DEFAULT 0,
        LastStockUpdate DATETIME2 NOT NULL DEFAULT GETDATE()
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Products')
BEGIN
    CREATE TABLE Products (
        Id INT PRIMARY KEY IDENTITY(1,1),
        Code NVARCHAR(50) NOT NULL,
        Name NVARCHAR(200) NOT NULL,
        Barcode NVARCHAR(100) NULL,
        PurchasePrice DECIMAL(18,2) NOT NULL DEFAULT 0,
        SalePrice DECIMAL(18,2) NOT NULL DEFAULT 0,
        VatRate DECIMAL(18,2) NOT NULL DEFAULT 20,
        Unit NVARCHAR(50) NOT NULL DEFAULT 'Adet',
        IsActive BIT NOT NULL DEFAULT 1,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'StockMovements')
BEGIN
    CREATE TABLE StockMovements (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        WarehouseId INT NOT NULL,
        ProductId INT NOT NULL,
        Type NVARCHAR(50) NOT NULL,
        Quantity DECIMAL(18,2) NOT NULL,
        Description NVARCHAR(255) NULL,
        DocumentNo NVARCHAR(100) NULL,
        Category NVARCHAR(100) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Sales')
BEGIN
    CREATE TABLE Sales (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        CustomerId INT NULL,
        WarehouseId INT NOT NULL DEFAULT 1,
        SaleDate DATETIME2 NOT NULL DEFAULT GETDATE(),
        SubTotal DECIMAL(18,2) NOT NULL DEFAULT 0,
        VatTotal DECIMAL(18,2) NOT NULL DEFAULT 0,
        GrandTotal DECIMAL(18,2) NOT NULL DEFAULT 0,
        PaymentType NVARCHAR(50) NOT NULL DEFAULT 'Nakit',
        CashAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
        CardAmount DECIMAL(18,2) NOT NULL DEFAULT 0
    );
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Sales' AND COLUMN_NAME = 'CashAmount')
        ALTER TABLE Sales ADD CashAmount DECIMAL(18,2) NOT NULL DEFAULT 0;
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Sales' AND COLUMN_NAME = 'CardAmount')
        ALTER TABLE Sales ADD CardAmount DECIMAL(18,2) NOT NULL DEFAULT 0;
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SaleItems')
BEGIN
    CREATE TABLE SaleItems (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        SaleId BIGINT NOT NULL,
        ProductId INT NOT NULL,
        Quantity DECIMAL(18,2) NOT NULL,
        UnitPrice DECIMAL(18,2) NOT NULL,
        VatRate DECIMAL(18,2) NOT NULL DEFAULT 20,
        UnitCost DECIMAL(18,2) NOT NULL DEFAULT 0
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CashSessions')
BEGIN
    CREATE TABLE CashSessions (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        SessionDate DATE NOT NULL,
        OpenedAt DATETIME2 NOT NULL,
        OpeningCash DECIMAL(18,2) NOT NULL,
        ClosedAt DATETIME2 NULL,
        Status NVARCHAR(50) NOT NULL DEFAULT 'Açık',
        Notes NVARCHAR(MAX) NULL
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CashAccounts')
BEGIN
    CREATE TABLE CashAccounts (
        Id INT PRIMARY KEY IDENTITY(1,1),
        Name NVARCHAR(100) NOT NULL,
        Balance DECIMAL(18,2) NOT NULL DEFAULT 0
    );
END

IF NOT EXISTS (SELECT 1 FROM CashAccounts WHERE Id = 1)
BEGIN
    SET IDENTITY_INSERT CashAccounts ON;
    INSERT INTO CashAccounts (Id, Name, Balance) VALUES (1, 'Merkez Kasa', 0);
    SET IDENTITY_INSERT CashAccounts OFF;
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Expenses')
BEGIN
    CREATE TABLE Expenses (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        Category NVARCHAR(100) NOT NULL,
        Amount DECIMAL(18,2) NOT NULL,
        Description NVARCHAR(500) NULL,
        ExpenseDate DATETIME2 NOT NULL DEFAULT GETDATE()
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CashTransactions')
BEGIN
    CREATE TABLE CashTransactions (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        SessionId BIGINT NULL,
        CashAccountId INT NULL DEFAULT 1,
        Type NVARCHAR(50) NOT NULL,
        Amount DECIMAL(18,2) NOT NULL,
        Description NVARCHAR(255) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
    );
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('CashTransactions') AND name = 'SessionId')
    ALTER TABLE CashTransactions ADD SessionId BIGINT NULL;
ALTER TABLE CashTransactions ALTER COLUMN Type NVARCHAR(100) NOT NULL;

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Closures')
BEGIN
    CREATE TABLE Closures (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        ClosureDate DATE NOT NULL,
        ClosedAt DATETIME2 NOT NULL,
        SalesCount INT NOT NULL DEFAULT 0,
        TotalRevenue DECIMAL(18,2) NOT NULL DEFAULT 0,
        TotalCash DECIMAL(18,2) NOT NULL DEFAULT 0,
        TotalCreditCard DECIMAL(18,2) NOT NULL DEFAULT 0,
        TotalOnAccount DECIMAL(18,2) NOT NULL DEFAULT 0,
        TotalCost DECIMAL(18,2) NOT NULL DEFAULT 0,
        TotalExpenses DECIMAL(18,2) NOT NULL DEFAULT 0,
        TotalProfit DECIMAL(18,2) NOT NULL DEFAULT 0,
        Notes NVARCHAR(MAX) NULL
    );
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Closures') AND name = 'ZReportCash')
    ALTER TABLE Closures ADD ZReportCash DECIMAL(18,2) NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Closures') AND name = 'ZReportCredit')
    ALTER TABLE Closures ADD ZReportCredit DECIMAL(18,2) NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Closures') AND name = 'CashDiff')
    ALTER TABLE Closures ADD CashDiff DECIMAL(18,2) NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Closures') AND name = 'CreditDiff')
    ALTER TABLE Closures ADD CreditDiff DECIMAL(18,2) NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Closures') AND name = 'CashExpense')
    ALTER TABLE Closures ADD CashExpense DECIMAL(18,2) NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Closures') AND name = 'CreditExpense')
    ALTER TABLE Closures ADD CreditExpense DECIMAL(18,2) NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Closures') AND name = 'VatAmount')
    ALTER TABLE Closures ADD VatAmount DECIMAL(18,2) NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Closures') AND name = 'PocketCash')
    ALTER TABLE Closures ADD PocketCash DECIMAL(18,2) NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Closures') AND name = 'PocketCredit')
    ALTER TABLE Closures ADD PocketCredit DECIMAL(18,2) NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Closures') AND name = 'IbanAmount')
    ALTER TABLE Closures ADD IbanAmount DECIMAL(18,2) NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Closures') AND name = 'ExpectedBankPayment')
    ALTER TABLE Closures ADD ExpectedBankPayment DECIMAL(18,2) NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Closures') AND name = 'Source')
    ALTER TABLE Closures ADD Source NVARCHAR(100) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Closures') AND name = 'SourceSheet')
    ALTER TABLE Closures ADD SourceSheet NVARCHAR(100) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Closures') AND name = 'SourceRow')
    ALTER TABLE Closures ADD SourceRow INT NULL;

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ImportWarnings')
BEGIN
    CREATE TABLE ImportWarnings (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        ClosureDate DATE NOT NULL,
        SheetName NVARCHAR(100) NULL,
        RowIndex INT NULL,
        WarningType NVARCHAR(100) NOT NULL,
        Message NVARCHAR(500) NOT NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CariAccounts')
BEGIN
    CREATE TABLE CariAccounts (
        Id INT PRIMARY KEY IDENTITY(1,1),
        Code NVARCHAR(50) NOT NULL,
        Name NVARCHAR(150) NOT NULL,
        Type NVARCHAR(50) NOT NULL,
        Phone NVARCHAR(50) NULL,
        Email NVARCHAR(100) NULL,
        Address NVARCHAR(255) NULL,
        TaxNo NVARCHAR(50) NULL,
        TaxOffice NVARCHAR(100) NULL,
        Balance DECIMAL(18,2) NOT NULL DEFAULT 0,
        IsActive BIT NOT NULL DEFAULT 1,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CariTransactions')
BEGIN
    CREATE TABLE CariTransactions (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        CariAccountId INT NOT NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        TransactionType NVARCHAR(50) NOT NULL,
        DocumentNo NVARCHAR(100) NULL,
        Debit DECIMAL(18,2) NOT NULL DEFAULT 0,
        Credit DECIMAL(18,2) NOT NULL DEFAULT 0,
        BalanceAfter DECIMAL(18,2) NOT NULL DEFAULT 0,
        Description NVARCHAR(255) NULL
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'BankAccounts')
BEGIN
    CREATE TABLE BankAccounts (
        Id INT PRIMARY KEY IDENTITY(1,1),
        BankName NVARCHAR(100) NOT NULL,
        AccountName NVARCHAR(100) NOT NULL,
        Iban NVARCHAR(100) NULL,
        AccountNumber NVARCHAR(50) NULL,
        Branch NVARCHAR(100) NULL,
        Currency NVARCHAR(20) NOT NULL DEFAULT 'TRY',
        OpeningBalance DECIMAL(18,2) NOT NULL DEFAULT 0,
        CurrentBalance DECIMAL(18,2) NOT NULL DEFAULT 0,
        ActualBalance DECIMAL(18,2) NOT NULL DEFAULT 0,
        PosCommissionRate DECIMAL(18,2) NOT NULL DEFAULT 3.99,
        PosValourDays INT NOT NULL DEFAULT 2,
        IsDefault BIT NOT NULL DEFAULT 0,
        IsActive BIT NOT NULL DEFAULT 1,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'BankTransactions')
BEGIN
    CREATE TABLE BankTransactions (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        BankAccountId INT NOT NULL,
        BankAccountName NVARCHAR(150) NOT NULL,
        TransactionType NVARCHAR(50) NOT NULL,
        Direction NVARCHAR(20) NOT NULL DEFAULT 'Giriş',
        Inflow DECIMAL(18,2) NOT NULL DEFAULT 0,
        Outflow DECIMAL(18,2) NOT NULL DEFAULT 0,
        BalanceAfter DECIMAL(18,2) NOT NULL DEFAULT 0,
        Description NVARCHAR(255) NULL,
        PaymentMethod NVARCHAR(50) NOT NULL DEFAULT 'Havale',
        ReferenceId BIGINT NULL,
        ReferenceType NVARCHAR(50) NULL,
        [User] NVARCHAR(100) NOT NULL DEFAULT 'Yönetici'
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'PosTransactions')
BEGIN
    CREATE TABLE PosTransactions (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        SaleId BIGINT NULL,
        SaleDate DATETIME2 NOT NULL DEFAULT GETDATE(),
        GrossAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
        CommissionRate DECIMAL(18,2) NOT NULL DEFAULT 3.99,
        CommissionAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
        NetAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
        ExpectedDate DATETIME2 NOT NULL DEFAULT GETDATE(),
        ActualPaymentDate DATETIME2 NULL,
        ActualPaidAmount DECIMAL(18,2) NULL,
        BankAccountId INT NULL,
        BankAccountName NVARCHAR(150) NULL,
        Status NVARCHAR(50) NOT NULL DEFAULT 'Bekliyor',
        Description NVARCHAR(255) NULL
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Invoices')
BEGIN
    CREATE TABLE Invoices (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        InvoiceNo NVARCHAR(50) NOT NULL,
        Type NVARCHAR(50) NOT NULL,
        InvoiceDate DATETIME2 NOT NULL,
        SupplierOrCustomer NVARCHAR(150) NOT NULL,
        TaxNo NVARCHAR(50) NULL,
        WarehouseId INT NOT NULL DEFAULT 1,
        WarehouseName NVARCHAR(100) NOT NULL DEFAULT 'Ana Depo',
        SubTotal DECIMAL(18,2) NOT NULL DEFAULT 0,
        VatTotal DECIMAL(18,2) NOT NULL DEFAULT 0,
        GrandTotal DECIMAL(18,2) NOT NULL DEFAULT 0,
        PaymentType NVARCHAR(50) NOT NULL DEFAULT 'Nakit',
        PaymentStatus NVARCHAR(50) NOT NULL DEFAULT 'Ödendi',
        BankAccountId INT NULL,
        BankAccountName NVARCHAR(100) NULL,
        CurrentAccountName NVARCHAR(150) NULL,
        Notes NVARCHAR(MAX) NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
    );
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Invoices' AND COLUMN_NAME = 'CariAccountId')
        ALTER TABLE Invoices ADD CariAccountId INT NULL;
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Invoices' AND COLUMN_NAME = 'PaidAmount')
        ALTER TABLE Invoices ADD PaidAmount DECIMAL(18,2) NOT NULL DEFAULT 0;
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Invoices' AND COLUMN_NAME = 'RemainingAmount')
        ALTER TABLE Invoices ADD RemainingAmount DECIMAL(18,2) NOT NULL DEFAULT 0;
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CariTransactions' AND COLUMN_NAME = 'InvoiceId')
    ALTER TABLE CariTransactions ADD InvoiceId BIGINT NULL;
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CariTransactions' AND COLUMN_NAME = 'SaleId')
    ALTER TABLE CariTransactions ADD SaleId BIGINT NULL;
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CariTransactions' AND COLUMN_NAME = 'PaymentMethod')
    ALTER TABLE CariTransactions ADD PaymentMethod NVARCHAR(50) NULL;
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CariTransactions' AND COLUMN_NAME = 'TransactionType')
    ALTER TABLE CariTransactions ADD TransactionType NVARCHAR(100) NULL;
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CariTransactions' AND COLUMN_NAME = 'Debit')
    ALTER TABLE CariTransactions ADD Debit DECIMAL(18,2) NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CariTransactions' AND COLUMN_NAME = 'Credit')
    ALTER TABLE CariTransactions ADD Credit DECIMAL(18,2) NOT NULL DEFAULT 0;

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CashTransactions' AND COLUMN_NAME = 'SourceDocumentType')
    ALTER TABLE CashTransactions ADD SourceDocumentType NVARCHAR(50) NULL;
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CashTransactions' AND COLUMN_NAME = 'SourceDocumentId')
    ALTER TABLE CashTransactions ADD SourceDocumentId BIGINT NULL;

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StockMovements' AND COLUMN_NAME = 'SourceDocumentType')
    ALTER TABLE StockMovements ADD SourceDocumentType NVARCHAR(50) NULL;
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'StockMovements' AND COLUMN_NAME = 'SourceDocumentId')
    ALTER TABLE StockMovements ADD SourceDocumentId BIGINT NULL;

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'InvoiceItems')
BEGIN
    CREATE TABLE InvoiceItems (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        InvoiceId BIGINT NOT NULL,
        ProductId INT NOT NULL,
        ProductCode NVARCHAR(50) NULL,
        ProductName NVARCHAR(150) NOT NULL,
        Barcode NVARCHAR(50) NULL,
        Quantity DECIMAL(18,2) NOT NULL DEFAULT 1,
        UnitPrice DECIMAL(18,2) NOT NULL DEFAULT 0,
        VatRate DECIMAL(18,2) NOT NULL DEFAULT 20,
        LineTotal DECIMAL(18,2) NOT NULL DEFAULT 0
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'GeneralExpenses')
BEGIN
    CREATE TABLE GeneralExpenses (
        Id INT PRIMARY KEY IDENTITY(1,1),
        Title NVARCHAR(150) NOT NULL,
        Category NVARCHAR(50) NOT NULL DEFAULT 'Kira',
        MonthlyAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
        DayOfMonth INT NOT NULL DEFAULT 15,
        Notes NVARCHAR(MAX) NULL,
        IsActive BIT NOT NULL DEFAULT 1,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'FinancialDebts')
BEGIN
    CREATE TABLE FinancialDebts (
        Id INT PRIMARY KEY IDENTITY(1,1),
        Title NVARCHAR(150) NOT NULL,
        Creditor NVARCHAR(150) NOT NULL,
        DebtType NVARCHAR(50) NOT NULL DEFAULT 'Kredi / Borç',
        BankAccountId INT NULL,
        TotalAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
        RemainingAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
        PrincipalAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
        AccruedInterest DECIMAL(18,2) NOT NULL DEFAULT 0,
        AccruedTax DECIMAL(18,2) NOT NULL DEFAULT 0,
        MinPaymentAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
        InterestRate DECIMAL(5,2) NOT NULL DEFAULT 0,
        LateInterestRate DECIMAL(5,2) NOT NULL DEFAULT 0,
        InstallmentAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
        TotalInstallments INT NOT NULL DEFAULT 1,
        RemainingInstallments INT NOT NULL DEFAULT 1,
        DayOfMonth INT NOT NULL DEFAULT 15,
        StartDate DATETIME2 NOT NULL DEFAULT GETDATE(),
        LastInterestCalculationDate DATETIME2 NULL,
        CutoffDate DATETIME2 NULL,
        Status NVARCHAR(50) NOT NULL DEFAULT 'Devam Ediyor',
        Notes NVARCHAR(MAX) NULL,
        IsActive BIT NOT NULL DEFAULT 1,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'FinancialReminders')
BEGIN
    CREATE TABLE FinancialReminders (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        Title NVARCHAR(150) NOT NULL,
        Category NVARCHAR(50) NOT NULL DEFAULT 'Kira',
        ReminderType NVARCHAR(50) NOT NULL DEFAULT 'Genel Gider',
        GeneralExpenseId INT NULL,
        DebtId INT NULL,
        Amount DECIMAL(18,2) NOT NULL DEFAULT 0,
        MinPayment DECIMAL(18,2) NULL,
        AccruedInterest DECIMAL(18,2) NULL,
        TaxAmount DECIMAL(18,2) NULL,
        PrincipalPaid DECIMAL(18,2) NULL,
        CurrentTotalDebt DECIMAL(18,2) NULL,
        InterestStartDate DATETIME2 NULL,
        DayOfMonth INT NOT NULL DEFAULT 15,
        DueDate DATETIME2 NOT NULL DEFAULT GETDATE(),
        IsRecurringMonthly BIT NOT NULL DEFAULT 1,
        Status NVARCHAR(50) NOT NULL DEFAULT 'Bekliyor',
        PaidDate DATETIME2 NULL,
        PaidFrom NVARCHAR(100) NULL,
        BankAccountId INT NULL,
        InvoiceNo NVARCHAR(50) NULL,
        RemainingDebtAfter DECIMAL(18,2) NULL,
        Description NVARCHAR(MAX) NULL,
        IsDeleted BIT NOT NULL DEFAULT 0,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'InterestRates')
BEGIN
    CREATE TABLE InterestRates (
        Id INT PRIMARY KEY IDENTITY(1,1),
        BankAccountId INT NULL,
        AccountType NVARCHAR(50) NOT NULL,
        InterestType NVARCHAR(50) NOT NULL,
        Rate DECIMAL(5,2) NOT NULL,
        EffectiveDate DATETIME2 NOT NULL DEFAULT GETDATE(),
        EndDate DATETIME2 NULL,
        BsmvRate DECIMAL(5,2) NOT NULL DEFAULT 5.00,
        KkdfRate DECIMAL(5,2) NOT NULL DEFAULT 15.00,
        IsActive BIT NOT NULL DEFAULT 1,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
    );

    INSERT INTO InterestRates (AccountType, InterestType, Rate, EffectiveDate, BsmvRate, KkdfRate, IsActive)
    VALUES 
    ('Kredi Kartı', 'Akdi Faiz', 4.25, '2024-01-01', 5.00, 15.00, 1),
    ('Kredi Kartı', 'Gecikme Faizi', 4.75, '2024-01-01', 5.00, 15.00, 1),
    ('KMH / Ek Hesap', 'Akdi Faiz', 5.00, '2024-01-01', 5.00, 15.00, 1),
    ('KMH / Ek Hesap', 'Gecikme Faizi', 5.50, '2024-01-01', 5.00, 15.00, 1),
    ('Kredi', 'Akdi Faiz', 3.75, '2024-01-01', 5.00, 15.00, 1);
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'InterestAccruals')
BEGIN
    CREATE TABLE InterestAccruals (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        DebtId INT NOT NULL,
        FromDate DATETIME2 NOT NULL,
        ToDate DATETIME2 NOT NULL,
        Days INT NOT NULL,
        Rate DECIMAL(5,2) NOT NULL,
        PrincipalAmount DECIMAL(18,2) NOT NULL,
        CalculatedInterest DECIMAL(18,2) NOT NULL,
        BsmvAmount DECIMAL(18,2) NOT NULL,
        KkdfAmount DECIMAL(18,2) NOT NULL,
        TotalTax DECIMAL(18,2) NOT NULL,
        TotalAccrual DECIMAL(18,2) NOT NULL,
        IsApplied BIT NOT NULL DEFAULT 1,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DebtTransactions')
BEGIN
    CREATE TABLE DebtTransactions (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        DebtId INT NOT NULL,
        TransactionDate DATETIME2 NOT NULL DEFAULT GETDATE(),
        TransactionType NVARCHAR(50) NOT NULL,
        Amount DECIMAL(18,2) NOT NULL,
        PrincipalChange DECIMAL(18,2) NOT NULL DEFAULT 0,
        InterestChange DECIMAL(18,2) NOT NULL DEFAULT 0,
        TaxChange DECIMAL(18,2) NOT NULL DEFAULT 0,
        PreviousBalance DECIMAL(18,2) NOT NULL DEFAULT 0,
        NewBalance DECIMAL(18,2) NOT NULL DEFAULT 0,
        Description NVARCHAR(255) NULL,
        ReferenceId BIGINT NULL,
        ReferenceType NVARCHAR(50) NULL,
        UserId INT NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Payments')
BEGIN
    CREATE TABLE Payments (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        DebtId INT NOT NULL,
        BankAccountId INT NULL,
        PaymentDate DATETIME2 NOT NULL DEFAULT GETDATE(),
        TotalPaid DECIMAL(18,2) NOT NULL,
        PaymentType NVARCHAR(50) NOT NULL DEFAULT 'Kısmi',
        Notes NVARCHAR(MAX) NULL,
        CreatedBy NVARCHAR(100) NOT NULL DEFAULT 'Yönetici',
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'PaymentAllocations')
BEGIN
    CREATE TABLE PaymentAllocations (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        PaymentId BIGINT NOT NULL,
        DebtId INT NOT NULL,
        AllocatedInterest DECIMAL(18,2) NOT NULL DEFAULT 0,
        AllocatedTax DECIMAL(18,2) NOT NULL DEFAULT 0,
        AllocatedExpense DECIMAL(18,2) NOT NULL DEFAULT 0,
        AllocatedPrincipal DECIMAL(18,2) NOT NULL DEFAULT 0,
        RemainingPrincipalAfter DECIMAL(18,2) NOT NULL DEFAULT 0,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'FinancialAuditLogs')
BEGIN
    CREATE TABLE FinancialAuditLogs (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        ActionType NVARCHAR(50) NOT NULL,
        EntityName NVARCHAR(100) NOT NULL,
        EntityId BIGINT NULL,
        BankAccountId INT NULL,
        DebtId INT NULL,
        OldValue NVARCHAR(MAX) NULL,
        NewValue NVARCHAR(MAX) NULL,
        Description NVARCHAR(500) NOT NULL,
        UserId INT NULL,
        UserName NVARCHAR(100) NOT NULL DEFAULT 'Sistem',
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
    );
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ZReportEmails')
BEGIN
    CREATE TABLE ZReportEmails (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        ClosureId BIGINT NULL,
        ReportDate DATE NOT NULL,
        RecipientEmail NVARCHAR(255) NOT NULL,
        Subject NVARCHAR(255) NOT NULL,
        PdfFilePath NVARCHAR(500) NULL,
        Status NVARCHAR(50) NOT NULL DEFAULT 'Bekliyor',
        ErrorMessage NVARCHAR(MAX) NULL,
        RetryCount INT NOT NULL DEFAULT 0,
        SentAt DATETIME2 NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
    );

    CREATE NONCLUSTERED INDEX IX_ZReportEmails_ClosureId ON ZReportEmails (ClosureId);
    CREATE NONCLUSTERED INDEX IX_ZReportEmails_ReportDate ON ZReportEmails (ReportDate);
    CREATE NONCLUSTERED INDEX IX_ZReportEmails_Status ON ZReportEmails (Status);
END

-- ==========================================================
-- PERFORMANS VE RAM OPTİMİZASYON İNDEKSLERİ
-- ==========================================================
-- 1. Ürünler
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Products_Barcode' AND object_id = OBJECT_ID('Products'))
    CREATE NONCLUSTERED INDEX IX_Products_Barcode ON Products (Barcode) WHERE Barcode IS NOT NULL;

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Products_Code' AND object_id = OBJECT_ID('Products'))
    CREATE NONCLUSTERED INDEX IX_Products_Code ON Products (Code);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Products_IsActive' AND object_id = OBJECT_ID('Products'))
    CREATE NONCLUSTERED INDEX IX_Products_IsActive ON Products (IsActive);

-- 2. Satış ve Satış Kalemleri
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Sales_SaleDate' AND object_id = OBJECT_ID('Sales'))
    CREATE NONCLUSTERED INDEX IX_Sales_SaleDate ON Sales (SaleDate DESC) INCLUDE (SubTotal, VatTotal, GrandTotal, CostTotal, PaymentType, CashAmount, CardAmount);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SaleItems_SaleId' AND object_id = OBJECT_ID('SaleItems'))
    CREATE NONCLUSTERED INDEX IX_SaleItems_SaleId ON SaleItems (SaleId) INCLUDE (ProductId, Quantity, UnitPrice, VatRate, UnitCost);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SaleItems_ProductId' AND object_id = OBJECT_ID('SaleItems'))
    CREATE NONCLUSTERED INDEX IX_SaleItems_ProductId ON SaleItems (ProductId);

-- 3. Kasa Oturumları ve Hareketleri
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CashSessions_SessionDate' AND object_id = OBJECT_ID('CashSessions'))
    CREATE NONCLUSTERED INDEX IX_CashSessions_SessionDate ON CashSessions (SessionDate DESC, Status);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CashTransactions_SessionId' AND object_id = OBJECT_ID('CashTransactions'))
    CREATE NONCLUSTERED INDEX IX_CashTransactions_SessionId ON CashTransactions (SessionId);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CashTransactions_CreatedAt' AND object_id = OBJECT_ID('CashTransactions'))
    CREATE NONCLUSTERED INDEX IX_CashTransactions_CreatedAt ON CashTransactions (CreatedAt DESC);

-- 4. Gün Sonu Kapanışları
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Closures_ClosureDate' AND object_id = OBJECT_ID('Closures'))
    CREATE NONCLUSTERED INDEX IX_Closures_ClosureDate ON Closures (ClosureDate DESC);

-- 5. Banka ve POS Hareketleri
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_BankTransactions_BankAccountId' AND object_id = OBJECT_ID('BankTransactions'))
    CREATE NONCLUSTERED INDEX IX_BankTransactions_BankAccountId ON BankTransactions (BankAccountId, CreatedAt DESC);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_PosTransactions_Status_ExpectedDate' AND object_id = OBJECT_ID('PosTransactions'))
    CREATE NONCLUSTERED INDEX IX_PosTransactions_Status_ExpectedDate ON PosTransactions (Status, ExpectedDate);

-- 6. Cari ve Depo Stok Hareketleri
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CariTransactions_CariAccountId' AND object_id = OBJECT_ID('CariTransactions'))
    CREATE NONCLUSTERED INDEX IX_CariTransactions_CariAccountId ON CariTransactions (CariAccountId, CreatedAt DESC);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_StockMovements_ProductId' AND object_id = OBJECT_ID('StockMovements'))
    CREATE NONCLUSTERED INDEX IX_StockMovements_ProductId ON StockMovements (ProductId, WarehouseId, CreatedAt DESC);
";
        await using var cmd = new SqlCommand(ddl, conn);
        await cmd.ExecuteNonQueryAsync();

        // SQL Server RAM tüketim tavanını yapılandır (1024 MB tavanı koyarak tüm PC RAM'ini sömürmesini engelle)
        await OptimizeSqlServerMemoryAsync(conn);
    }

    private async Task OptimizeSqlServerMemoryAsync(SqlConnection conn)
    {
        try
        {
            var memorySql = @"
BEGIN TRY
    EXEC sp_configure 'show advanced options', 1;
    RECONFIGURE WITH OVERRIDE;
    EXEC sp_configure 'max server memory (MB)', 1024;
    RECONFIGURE WITH OVERRIDE;
END TRY
BEGIN CATCH
END CATCH";
            await using var cmd = new SqlCommand(memorySql, conn);
            await cmd.ExecuteNonQueryAsync();
            System.Diagnostics.Debug.WriteLine("[DatabaseSyncService] SQL Server Max RAM 1024 MB olarak sınırlandırıldı.");
        }
        catch { }
    }

    public async Task MigrateAllJsonDataAsync()
    {
        await using var conn = new SqlConnection(_dbService.ConnectionString);
        await conn.OpenAsync();

        await MigrateUsersAsync(conn);
        await MigrateProductsAsync(conn);
        await MigrateWarehousesAsync(conn);
        await MigrateSalesAsync(conn);
        await MigrateCashSessionsAsync(conn);
        await MigrateCashTransactionsAsync(conn);
        await MigrateClosuresAsync(conn);
        await MigrateCariAsync(conn);
        await MigrateBankAsync(conn);
        await MigrateInvoicesAsync(conn);
        await MigrateFinancialDataAsync(conn);
    }

    private async Task MigrateUsersAsync(SqlConnection conn)
    {
        var file = Path.Combine(StorageDirectory, "users.json");
        if (!File.Exists(file)) return;

        var json = await File.ReadAllTextAsync(file);
        var users = JsonSerializer.Deserialize<List<AppUser>>(json);
        if (users == null) return;

        foreach (var u in users)
        {
            var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Users WHERE Id = @id OR Username = @u", conn);
            checkCmd.Parameters.AddWithValue("@id", u.Id);
            checkCmd.Parameters.AddWithValue("@u", u.Username);
            int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

            if (exists == 0)
            {
                var insSql = @"SET IDENTITY_INSERT Users ON;
                               INSERT INTO Users (Id, Username, FullName, Role, PinCode, IsActive, CreatedAt, PermissionsJson)
                               VALUES (@id, @u, @fn, @role, @pin, @active, @created, @perm);
                               SET IDENTITY_INSERT Users OFF;";
                var cmd = new SqlCommand(insSql, conn);
                cmd.Parameters.AddWithValue("@id", u.Id);
                cmd.Parameters.AddWithValue("@u", u.Username);
                cmd.Parameters.AddWithValue("@fn", u.FullName);
                cmd.Parameters.AddWithValue("@role", u.Role);
                cmd.Parameters.AddWithValue("@pin", u.PinCode);
                cmd.Parameters.AddWithValue("@active", u.IsActive);
                cmd.Parameters.AddWithValue("@created", u.CreatedAt);
                cmd.Parameters.AddWithValue("@perm", JsonSerializer.Serialize(u));
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }

    private async Task MigrateProductsAsync(SqlConnection conn)
    {
        var file = Path.Combine(StorageDirectory, "products.json");
        if (!File.Exists(file)) return;

        var json = await File.ReadAllTextAsync(file);
        var products = JsonSerializer.Deserialize<List<Product>>(json);
        if (products == null) return;

        foreach (var p in products)
        {
            var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Products WHERE Id = @id", conn);
            checkCmd.Parameters.AddWithValue("@id", p.Id);
            int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

            if (exists == 0)
            {
                var insSql = @"SET IDENTITY_INSERT Products ON;
                               INSERT INTO Products (Id, Code, Name, Barcode, PurchasePrice, SalePrice, VatRate, Unit, IsActive)
                               VALUES (@id, @code, @name, @bc, @pp, @sp, @vat, 'Adet', 1);
                               SET IDENTITY_INSERT Products OFF;";
                var cmd = new SqlCommand(insSql, conn);
                cmd.Parameters.AddWithValue("@id", p.Id);
                cmd.Parameters.AddWithValue("@code", p.Code);
                cmd.Parameters.AddWithValue("@name", p.Name);
                cmd.Parameters.AddWithValue("@bc", (object?)p.Barcode ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@pp", p.PurchasePrice);
                cmd.Parameters.AddWithValue("@sp", p.SalePrice);
                cmd.Parameters.AddWithValue("@vat", p.VatRate);
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }

    private async Task MigrateWarehousesAsync(SqlConnection conn)
    {
        var whFile = Path.Combine(StorageDirectory, "warehouses.json");
        if (File.Exists(whFile))
        {
            var json = await File.ReadAllTextAsync(whFile);
            var warehouses = JsonSerializer.Deserialize<List<Warehouse>>(json);
            if (warehouses != null)
            {
                foreach (var w in warehouses)
                {
                    var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Warehouses WHERE Id = @id", conn);
                    checkCmd.Parameters.AddWithValue("@id", w.Id);
                    int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

                    if (exists == 0)
                    {
                        var insSql = @"SET IDENTITY_INSERT Warehouses ON;
                                       INSERT INTO Warehouses (Id, Code, Name, Location, Description, IsDefault, IsActive, CreatedAt)
                                       VALUES (@id, @code, @name, @loc, @desc, @def, @act, @created);
                                       SET IDENTITY_INSERT Warehouses OFF;";
                        var cmd = new SqlCommand(insSql, conn);
                        cmd.Parameters.AddWithValue("@id", w.Id);
                        cmd.Parameters.AddWithValue("@code", w.Code);
                        cmd.Parameters.AddWithValue("@name", w.Name);
                        cmd.Parameters.AddWithValue("@loc", (object?)w.Location ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@desc", (object?)w.Description ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@def", w.IsDefault);
                        cmd.Parameters.AddWithValue("@act", w.IsActive);
                        cmd.Parameters.AddWithValue("@created", w.CreatedAt);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }

        var stockFile = Path.Combine(StorageDirectory, "warehouse_stocks.json");
        if (File.Exists(stockFile))
        {
            var json = await File.ReadAllTextAsync(stockFile);
            var stocks = JsonSerializer.Deserialize<List<WarehouseStock>>(json);
            if (stocks != null)
            {
                foreach (var s in stocks)
                {
                    var checkCmd = new SqlCommand("SELECT COUNT(*) FROM WarehouseStocks WHERE WarehouseId = @wid AND ProductId = @pid", conn);
                    checkCmd.Parameters.AddWithValue("@wid", s.WarehouseId);
                    checkCmd.Parameters.AddWithValue("@pid", s.ProductId);
                    int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                    if (exists == 0)
                    {
                        var insSql = @"INSERT INTO WarehouseStocks (WarehouseId, ProductId, Quantity)
                                       VALUES (@wid, @pid, @qty);";
                        var cmd = new SqlCommand(insSql, conn);
                        cmd.Parameters.AddWithValue("@wid", s.WarehouseId);
                        cmd.Parameters.AddWithValue("@pid", s.ProductId);
                        cmd.Parameters.AddWithValue("@qty", s.Quantity);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }
    }

    private async Task MigrateSalesAsync(SqlConnection conn)
    {
        var file = Path.Combine(StorageDirectory, "sales.json");
        if (!File.Exists(file)) return;

        var json = await File.ReadAllTextAsync(file);
        var sales = JsonSerializer.Deserialize<List<Sale>>(json);
        if (sales == null || sales.Count == 0) return;

        foreach (var s in sales)
        {
            var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Sales WHERE Id = @id", conn);
            checkCmd.Parameters.AddWithValue("@id", s.Id);
            int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

            if (exists == 0)
            {
                var saleSql = @"SET IDENTITY_INSERT Sales ON;
                                INSERT INTO Sales (Id, WarehouseId, SaleDate, SubTotal, VatTotal, GrandTotal, CostTotal, PaymentType, CashAmount, CardAmount)
                                VALUES (@id, 1, @date, @sub, @vat, @grand, @cost, @type, @cash, @card);
                                SET IDENTITY_INSERT Sales OFF;";
                var cmd = new SqlCommand(saleSql, conn);
                cmd.Parameters.AddWithValue("@id", s.Id);
                cmd.Parameters.AddWithValue("@date", s.SaleDate);
                cmd.Parameters.AddWithValue("@sub", s.SubTotal);
                cmd.Parameters.AddWithValue("@vat", s.VatTotal);
                cmd.Parameters.AddWithValue("@grand", s.GrandTotal);
                cmd.Parameters.AddWithValue("@cost", s.CostTotal);
                cmd.Parameters.AddWithValue("@type", s.PaymentType);
                cmd.Parameters.AddWithValue("@cash", s.GetEffectiveCashAmount());
                cmd.Parameters.AddWithValue("@card", s.GetEffectiveCardAmount());
                await cmd.ExecuteNonQueryAsync();

                foreach (var item in s.Items)
                {
                    var itemSql = @"INSERT INTO SaleItems (SaleId, ProductId, Quantity, UnitPrice, VatRate, UnitCost)
                                    VALUES (@sid, @pid, @qty, @up, @vat, @uc)";
                    var itemCmd = new SqlCommand(itemSql, conn);
                    itemCmd.Parameters.AddWithValue("@sid", s.Id);
                    itemCmd.Parameters.AddWithValue("@pid", item.ProductId);
                    itemCmd.Parameters.AddWithValue("@qty", item.Quantity);
                    itemCmd.Parameters.AddWithValue("@up", item.UnitPrice);
                    itemCmd.Parameters.AddWithValue("@vat", item.VatRate);
                    itemCmd.Parameters.AddWithValue("@uc", item.UnitCost);
                    await itemCmd.ExecuteNonQueryAsync();
                }
            }
        }
    }

    private async Task MigrateCashSessionsAsync(SqlConnection conn)
    {
        var file = Path.Combine(StorageDirectory, "cash_sessions.json");
        if (!File.Exists(file)) return;

        var json = await File.ReadAllTextAsync(file);
        var sessions = JsonSerializer.Deserialize<List<CashSession>>(json);
        if (sessions == null) return;

        foreach (var s in sessions)
        {
            var checkCmd = new SqlCommand("SELECT COUNT(*) FROM CashSessions WHERE Id = @id", conn);
            checkCmd.Parameters.AddWithValue("@id", s.Id);
            int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

            if (exists == 0)
            {
                var insSql = @"SET IDENTITY_INSERT CashSessions ON;
                               INSERT INTO CashSessions (Id, SessionDate, OpenedAt, OpeningCash, ClosedAt, Status, Notes)
                               VALUES (@id, @date, @opened, @cash, @closed, @stat, @notes);
                               SET IDENTITY_INSERT CashSessions OFF;";
                var cmd = new SqlCommand(insSql, conn);
                cmd.Parameters.AddWithValue("@id", s.Id);
                cmd.Parameters.AddWithValue("@date", s.SessionDate.Date);
                cmd.Parameters.AddWithValue("@opened", s.OpenedAt);
                cmd.Parameters.AddWithValue("@cash", s.OpeningCash);
                cmd.Parameters.AddWithValue("@closed", (object?)s.ClosedAt ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@stat", s.Status);
                cmd.Parameters.AddWithValue("@notes", (object?)s.Notes ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }

    private async Task MigrateCashTransactionsAsync(SqlConnection conn)
    {
        var file = Path.Combine(StorageDirectory, "cash_transactions.json");
        if (!File.Exists(file)) return;

        var cmdCount = new SqlCommand("SELECT COUNT(*) FROM CashTransactions", conn);
        int count = Convert.ToInt32(await cmdCount.ExecuteScalarAsync());
        if (count > 0) return;

        var json = await File.ReadAllTextAsync(file);
        var transactions = JsonSerializer.Deserialize<List<CashTransaction>>(json);
        if (transactions == null) return;

        foreach (var t in transactions)
        {
            var insSql = @"INSERT INTO CashTransactions (SessionId, CashAccountId, Type, Amount, Description, CreatedAt)
                           VALUES (@sid, 1, @type, @amt, @desc, @created)";
            var cmd = new SqlCommand(insSql, conn);
            cmd.Parameters.AddWithValue("@sid", t.SessionId);
            cmd.Parameters.AddWithValue("@type", t.Type);
            cmd.Parameters.AddWithValue("@amt", t.Amount);
            cmd.Parameters.AddWithValue("@desc", (object?)t.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@created", t.CreatedAt);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task MigrateClosuresAsync(SqlConnection conn)
    {
        var file = Path.Combine(StorageDirectory, "closures.json");
        if (!File.Exists(file)) return;

        var json = await File.ReadAllTextAsync(file);
        var closures = JsonSerializer.Deserialize<List<DailyClosure>>(json);
        if (closures == null) return;

        foreach (var c in closures)
        {
            var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Closures WHERE Id = @id", conn);
            checkCmd.Parameters.AddWithValue("@id", c.Id);
            int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

            if (exists == 0)
            {
                var insSql = @"SET IDENTITY_INSERT Closures ON;
                               INSERT INTO Closures (Id, ClosureDate, ClosedAt, SalesCount, TotalRevenue, TotalCash, TotalCreditCard, TotalOnAccount, TotalCost, TotalExpenses, TotalProfit, Notes)
                               VALUES (@id, @date, @closed, @cnt, @rev, @cash, @card, @onAcc, @cost, @exp, @profit, @notes);
                               SET IDENTITY_INSERT Closures OFF;";
                var cmd = new SqlCommand(insSql, conn);
                cmd.Parameters.AddWithValue("@id", c.Id);
                cmd.Parameters.AddWithValue("@date", c.ClosureDate.Date);
                cmd.Parameters.AddWithValue("@closed", c.ClosedAt);
                cmd.Parameters.AddWithValue("@cnt", c.SalesCount);
                cmd.Parameters.AddWithValue("@rev", c.TotalRevenue);
                cmd.Parameters.AddWithValue("@cash", c.TotalCash);
                cmd.Parameters.AddWithValue("@card", c.TotalCreditCard);
                cmd.Parameters.AddWithValue("@onAcc", c.TotalOnAccount);
                cmd.Parameters.AddWithValue("@cost", c.TotalCost);
                cmd.Parameters.AddWithValue("@exp", c.TotalExpenses);
                cmd.Parameters.AddWithValue("@profit", c.TotalProfit);
                cmd.Parameters.AddWithValue("@notes", (object?)c.Notes ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }

    private async Task MigrateCariAsync(SqlConnection conn)
    {
        var file = Path.Combine(StorageDirectory, "cari_accounts.json");
        if (!File.Exists(file)) return;

        var json = await File.ReadAllTextAsync(file);
        var accounts = JsonSerializer.Deserialize<List<CariAccount>>(json);
        if (accounts == null) return;

        foreach (var c in accounts)
        {
            var checkCmd = new SqlCommand("SELECT COUNT(*) FROM CariAccounts WHERE Id = @id", conn);
            checkCmd.Parameters.AddWithValue("@id", c.Id);
            int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

            if (exists == 0)
            {
                var insSql = @"SET IDENTITY_INSERT CariAccounts ON;
                               INSERT INTO CariAccounts (Id, Code, Name, Type, Phone, Email, Address, TaxNo, TaxOffice, Balance, IsActive, CreatedAt)
                               VALUES (@id, @code, @name, @type, @phone, @email, @addr, @taxNo, @taxOff, @bal, @act, @created);
                               SET IDENTITY_INSERT CariAccounts OFF;";
                var cmd = new SqlCommand(insSql, conn);
                cmd.Parameters.AddWithValue("@id", c.Id);
                cmd.Parameters.AddWithValue("@code", c.Code);
                cmd.Parameters.AddWithValue("@name", c.Name);
                cmd.Parameters.AddWithValue("@type", c.Type);
                cmd.Parameters.AddWithValue("@phone", (object?)c.Phone ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@email", (object?)c.Email ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@addr", (object?)c.Address ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@taxNo", (object?)c.TaxNo ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@taxOff", (object?)c.TaxOffice ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@bal", c.Balance);
                cmd.Parameters.AddWithValue("@act", c.IsActive);
                cmd.Parameters.AddWithValue("@created", c.CreatedAt);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        var tFile = Path.Combine(StorageDirectory, "cari_transactions.json");
        if (File.Exists(tFile))
        {
            var tJson = await File.ReadAllTextAsync(tFile);
            var transactions = JsonSerializer.Deserialize<List<CariTransaction>>(tJson);
            if (transactions != null)
            {
                foreach (var t in transactions)
                {
                    var checkCmd = new SqlCommand("SELECT COUNT(*) FROM CariTransactions WHERE Id = @id", conn);
                    checkCmd.Parameters.AddWithValue("@id", t.Id);
                    int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

                    if (exists == 0)
                    {
                        var insSql = @"SET IDENTITY_INSERT CariTransactions ON;
                                       INSERT INTO CariTransactions (Id, CariAccountId, CreatedAt, TransactionType, DocumentNo, Debit, Credit, BalanceAfter, Description)
                                       VALUES (@id, @cid, @created, @type, @doc, @deb, @cred, @bal, @desc);
                                       SET IDENTITY_INSERT CariTransactions OFF;";
                        var cmd = new SqlCommand(insSql, conn);
                        cmd.Parameters.AddWithValue("@id", t.Id);
                        cmd.Parameters.AddWithValue("@cid", t.CariAccountId);
                        cmd.Parameters.AddWithValue("@created", t.CreatedAt);
                        cmd.Parameters.AddWithValue("@type", t.TransactionType);
                        cmd.Parameters.AddWithValue("@doc", (object?)t.DocumentNo ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@deb", t.Debit);
                        cmd.Parameters.AddWithValue("@cred", t.Credit);
                        cmd.Parameters.AddWithValue("@bal", t.BalanceAfter);
                        cmd.Parameters.AddWithValue("@desc", (object?)t.Description ?? DBNull.Value);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }
    }

    private async Task MigrateBankAsync(SqlConnection conn)
    {
        var file = Path.Combine(StorageDirectory, "bank_accounts.json");
        if (File.Exists(file))
        {
            var json = await File.ReadAllTextAsync(file);
            var banks = JsonSerializer.Deserialize<List<BankAccount>>(json);
            if (banks != null)
            {
                foreach (var b in banks)
                {
                    var checkCmd = new SqlCommand("SELECT COUNT(*) FROM BankAccounts WHERE Id = @id", conn);
                    checkCmd.Parameters.AddWithValue("@id", b.Id);
                    int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

                    if (exists == 0)
                    {
                        var insSql = @"SET IDENTITY_INSERT BankAccounts ON;
                                       INSERT INTO BankAccounts (Id, BankName, AccountName, Iban, AccountNumber, Branch, Currency, OpeningBalance, CurrentBalance, ActualBalance, PosCommissionRate, PosValourDays, IsDefault, IsActive, CreatedAt)
                                       VALUES (@id, @bn, @an, @iban, @accNo, @br, @cur, @opBal, @curBal, @actBal, @rate, @days, @def, @act, @created);
                                       SET IDENTITY_INSERT BankAccounts OFF;";
                        var cmd = new SqlCommand(insSql, conn);
                        cmd.Parameters.AddWithValue("@id", b.Id);
                        cmd.Parameters.AddWithValue("@bn", b.BankName);
                        cmd.Parameters.AddWithValue("@an", b.AccountName);
                        cmd.Parameters.AddWithValue("@iban", (object?)b.Iban ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@accNo", (object?)b.AccountNumber ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@br", (object?)b.Branch ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@cur", b.Currency);
                        cmd.Parameters.AddWithValue("@opBal", b.OpeningBalance);
                        cmd.Parameters.AddWithValue("@curBal", b.CurrentBalance);
                        cmd.Parameters.AddWithValue("@actBal", b.ActualBalance);
                        cmd.Parameters.AddWithValue("@rate", b.PosCommissionRate);
                        cmd.Parameters.AddWithValue("@days", b.PosValourDays);
                        cmd.Parameters.AddWithValue("@def", b.IsDefault);
                        cmd.Parameters.AddWithValue("@act", b.IsActive);
                        cmd.Parameters.AddWithValue("@created", b.CreatedAt);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }

        var tFile = Path.Combine(StorageDirectory, "bank_transactions.json");
        if (File.Exists(tFile))
        {
            var tJson = await File.ReadAllTextAsync(tFile);
            var transactions = JsonSerializer.Deserialize<List<BankTransaction>>(tJson);
            if (transactions != null)
            {
                foreach (var t in transactions)
                {
                    var checkCmd = new SqlCommand("SELECT COUNT(*) FROM BankTransactions WHERE Id = @id", conn);
                    checkCmd.Parameters.AddWithValue("@id", t.Id);
                    int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

                    if (exists == 0)
                    {
                        var insSql = @"SET IDENTITY_INSERT BankTransactions ON;
                                       INSERT INTO BankTransactions (Id, CreatedAt, BankAccountId, BankAccountName, TransactionType, Direction, Inflow, Outflow, BalanceAfter, Description, PaymentMethod, ReferenceId, ReferenceType, [User])
                                       VALUES (@id, @created, @baid, @baname, @type, @dir, @inflow, @outflow, @bal, @desc, @pm, @refId, @refType, @user);
                                       SET IDENTITY_INSERT BankTransactions OFF;";
                        var cmd = new SqlCommand(insSql, conn);
                        cmd.Parameters.AddWithValue("@id", t.Id);
                        cmd.Parameters.AddWithValue("@created", t.CreatedAt);
                        cmd.Parameters.AddWithValue("@baid", t.BankAccountId);
                        cmd.Parameters.AddWithValue("@baname", t.BankAccountName);
                        cmd.Parameters.AddWithValue("@type", t.TransactionType);
                        cmd.Parameters.AddWithValue("@dir", t.Direction);
                        cmd.Parameters.AddWithValue("@inflow", t.Inflow);
                        cmd.Parameters.AddWithValue("@outflow", t.Outflow);
                        cmd.Parameters.AddWithValue("@bal", t.BalanceAfter);
                        cmd.Parameters.AddWithValue("@desc", (object?)t.Description ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@pm", t.PaymentMethod);
                        cmd.Parameters.AddWithValue("@refId", (object?)t.ReferenceId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@refType", (object?)t.ReferenceType ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@user", t.User);
                    }
                }
            }
        }

        var posFile = Path.Combine(StorageDirectory, "pos_transactions.json");
        if (File.Exists(posFile))
        {
            var pJson = await File.ReadAllTextAsync(posFile);
            var posList = JsonSerializer.Deserialize<List<PosTransaction>>(pJson);
            if (posList != null)
            {
                foreach (var p in posList)
                {
                    var checkCmd = new SqlCommand("SELECT COUNT(*) FROM PosTransactions WHERE Id = @id", conn);
                    checkCmd.Parameters.AddWithValue("@id", p.Id);
                    int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

                    if (exists == 0)
                    {
                        var insSql = @"SET IDENTITY_INSERT PosTransactions ON;
                                       INSERT INTO PosTransactions (Id, SaleId, SaleDate, GrossAmount, CommissionRate, CommissionAmount, NetAmount, ExpectedDate, ActualPaymentDate, ActualPaidAmount, BankAccountId, BankAccountName, Status, Description)
                                       VALUES (@id, @sid, @sdate, @gross, @rate, @camt, @net, @expDate, @actDate, @actPaid, @baid, @baname, @stat, @desc);
                                       SET IDENTITY_INSERT PosTransactions OFF;";
                        var cmd = new SqlCommand(insSql, conn);
                        cmd.Parameters.AddWithValue("@id", p.Id);
                        cmd.Parameters.AddWithValue("@sid", (object?)p.SaleId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@sdate", p.SaleDate);
                        cmd.Parameters.AddWithValue("@gross", p.GrossAmount);
                        cmd.Parameters.AddWithValue("@rate", p.CommissionRate);
                        cmd.Parameters.AddWithValue("@camt", p.CommissionAmount);
                        cmd.Parameters.AddWithValue("@net", p.NetAmount);
                        cmd.Parameters.AddWithValue("@expDate", p.ExpectedDate);
                        cmd.Parameters.AddWithValue("@actDate", (object?)p.ActualPaymentDate ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@actPaid", (object?)p.ActualPaidAmount ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@baid", (object?)p.BankAccountId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@baname", (object?)p.BankAccountName ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@stat", p.Status);
                        cmd.Parameters.AddWithValue("@desc", (object?)p.Description ?? DBNull.Value);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }
    }

    private async Task MigrateInvoicesAsync(SqlConnection conn)
    {
        var file = Path.Combine(StorageDirectory, "invoices.json");
        if (!File.Exists(file)) return;

        var json = await File.ReadAllTextAsync(file);
        var invoices = JsonSerializer.Deserialize<List<Invoice>>(json);
        if (invoices == null) return;

        foreach (var inv in invoices)
        {
            var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Invoices WHERE Id = @id", conn);
            checkCmd.Parameters.AddWithValue("@id", inv.Id);
            int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

            if (exists == 0)
            {
                var insSql = @"SET IDENTITY_INSERT Invoices ON;
                               INSERT INTO Invoices (Id, InvoiceNo, Type, InvoiceDate, SupplierOrCustomer, TaxNo, WarehouseId, WarehouseName, SubTotal, VatTotal, GrandTotal, PaymentType, PaymentStatus, BankAccountId, BankAccountName, CurrentAccountName, Notes, CreatedAt)
                               VALUES (@id, @no, @t, @date, @soc, @tax, @wid, @wn, @sub, @vat, @grand, @ptype, @pstat, @baid, @baname, @caname, @notes, GETDATE());
                               SET IDENTITY_INSERT Invoices OFF;";
                var cmd = new SqlCommand(insSql, conn);
                cmd.Parameters.AddWithValue("@id", inv.Id);
                cmd.Parameters.AddWithValue("@no", inv.InvoiceNo);
                cmd.Parameters.AddWithValue("@t", inv.Type);
                cmd.Parameters.AddWithValue("@date", inv.InvoiceDate);
                cmd.Parameters.AddWithValue("@soc", inv.SupplierOrCustomer);
                cmd.Parameters.AddWithValue("@tax", (object?)inv.TaxNo ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@wid", inv.WarehouseId);
                cmd.Parameters.AddWithValue("@wn", inv.WarehouseName);
                cmd.Parameters.AddWithValue("@sub", inv.SubTotal);
                cmd.Parameters.AddWithValue("@vat", inv.VatTotal);
                cmd.Parameters.AddWithValue("@grand", inv.GrandTotal);
                cmd.Parameters.AddWithValue("@ptype", inv.PaymentType);
                cmd.Parameters.AddWithValue("@pstat", inv.PaymentStatus);
                cmd.Parameters.AddWithValue("@baid", (object?)inv.BankAccountId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@baname", (object?)inv.BankAccountName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@caname", (object?)inv.CurrentAccountName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@notes", (object?)inv.Notes ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();

                foreach (var item in inv.Items)
                {
                    var itemSql = @"INSERT INTO InvoiceItems (InvoiceId, ProductId, ProductCode, ProductName, Barcode, Quantity, UnitPrice, VatRate, LineTotal)
                                    VALUES (@invid, @pid, @pc, @pn, @bc, @q, @up, @vat, @tot)";
                    var iCmd = new SqlCommand(itemSql, conn);
                    iCmd.Parameters.AddWithValue("@invid", inv.Id);
                    iCmd.Parameters.AddWithValue("@pid", item.ProductId);
                    iCmd.Parameters.AddWithValue("@pc", (object?)item.ProductCode ?? DBNull.Value);
                    iCmd.Parameters.AddWithValue("@pn", item.ProductName);
                    iCmd.Parameters.AddWithValue("@bc", (object?)item.Barcode ?? DBNull.Value);
                    iCmd.Parameters.AddWithValue("@q", item.Quantity);
                    iCmd.Parameters.AddWithValue("@up", item.UnitPrice);
                    iCmd.Parameters.AddWithValue("@vat", item.VatRate);
                    iCmd.Parameters.AddWithValue("@tot", item.LineTotal);
                    await iCmd.ExecuteNonQueryAsync();
                }
            }
        }
    }

    private async Task MigrateFinancialDataAsync(SqlConnection conn)
    {
        // 1. GeneralExpenses
        var expFile = Path.Combine(StorageDirectory, "general_expenses.json");
        if (File.Exists(expFile))
        {
            var json = await File.ReadAllTextAsync(expFile);
            var expenses = JsonSerializer.Deserialize<List<GeneralExpense>>(json);
            if (expenses != null)
            {
                foreach (var exp in expenses)
                {
                    var checkCmd = new SqlCommand("SELECT COUNT(*) FROM GeneralExpenses WHERE Id = @id", conn);
                    checkCmd.Parameters.AddWithValue("@id", exp.Id);
                    int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                    if (exists == 0)
                    {
                        var insSql = @"SET IDENTITY_INSERT GeneralExpenses ON;
                                       INSERT INTO GeneralExpenses (Id, Title, Category, MonthlyAmount, DayOfMonth, Notes, IsActive)
                                       VALUES (@id, @t, @c, @amt, @dom, @notes, @act);
                                       SET IDENTITY_INSERT GeneralExpenses OFF;";
                        var cmd = new SqlCommand(insSql, conn);
                        cmd.Parameters.AddWithValue("@id", exp.Id);
                        cmd.Parameters.AddWithValue("@t", exp.Title);
                        cmd.Parameters.AddWithValue("@c", exp.Category);
                        cmd.Parameters.AddWithValue("@amt", exp.MonthlyAmount);
                        cmd.Parameters.AddWithValue("@dom", exp.DayOfMonth);
                        cmd.Parameters.AddWithValue("@notes", (object?)exp.Description ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@act", exp.IsActive);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }

        // 2. FinancialDebts
        var debtsFile = Path.Combine(StorageDirectory, "financial_debts.json");
        if (File.Exists(debtsFile))
        {
            var json = await File.ReadAllTextAsync(debtsFile);
            var debts = JsonSerializer.Deserialize<List<FinancialDebt>>(json);
            if (debts != null)
            {
                foreach (var d in debts)
                {
                    var checkCmd = new SqlCommand("SELECT COUNT(*) FROM FinancialDebts WHERE Id = @id", conn);
                    checkCmd.Parameters.AddWithValue("@id", d.Id);
                    int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                    if (exists == 0)
                    {
                        var insSql = @"SET IDENTITY_INSERT FinancialDebts ON;
                                       INSERT INTO FinancialDebts (Id, Title, Creditor, DebtType, BankAccountId, TotalAmount, RemainingAmount, PrincipalAmount, AccruedInterest, AccruedTax, MinPaymentAmount, InterestRate, LateInterestRate, InstallmentAmount, TotalInstallments, RemainingInstallments, DayOfMonth, StartDate, Status, Notes, IsActive)
                                       VALUES (@id, @t, @cred, @dtype, @baid, @tot, @rem, @prin, @int, @tax, @min, @rate, @lrate, @inst, @tinst, @rinst, @dom, @sdate, @stat, @notes, @act);
                                       SET IDENTITY_INSERT FinancialDebts OFF;";
                        var cmd = new SqlCommand(insSql, conn);
                        cmd.Parameters.AddWithValue("@id", d.Id);
                        cmd.Parameters.AddWithValue("@t", d.Title);
                        cmd.Parameters.AddWithValue("@cred", d.Creditor);
                        cmd.Parameters.AddWithValue("@dtype", d.DebtType ?? "Kredi / Borç");
                        cmd.Parameters.AddWithValue("@baid", (object?)d.BankAccountId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@tot", d.TotalAmount);
                        cmd.Parameters.AddWithValue("@rem", d.RemainingAmount);
                        cmd.Parameters.AddWithValue("@prin", d.PrincipalAmount > 0 ? d.PrincipalAmount : d.RemainingAmount);
                        cmd.Parameters.AddWithValue("@int", d.AccruedInterest);
                        cmd.Parameters.AddWithValue("@tax", d.AccruedTax);
                        cmd.Parameters.AddWithValue("@min", d.MinPaymentAmount);
                        cmd.Parameters.AddWithValue("@rate", d.InterestRate);
                        cmd.Parameters.AddWithValue("@lrate", d.LateInterestRate);
                        cmd.Parameters.AddWithValue("@inst", d.InstallmentAmount);
                        cmd.Parameters.AddWithValue("@tinst", d.TotalInstallments);
                        cmd.Parameters.AddWithValue("@rinst", d.RemainingInstallments);
                        cmd.Parameters.AddWithValue("@dom", d.DayOfMonth);
                        cmd.Parameters.AddWithValue("@sdate", d.StartDate);
                        cmd.Parameters.AddWithValue("@stat", d.Status);
                        cmd.Parameters.AddWithValue("@notes", (object?)d.Notes ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@act", d.IsActive);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
            }
        }

        // 3. FinancialReminders
        var remFile = Path.Combine(StorageDirectory, "financial_reminders.json");
        if (File.Exists(remFile))
        {
            try
            {
                var json = await File.ReadAllTextAsync(remFile);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var reminders = JsonSerializer.Deserialize<List<FinancialReminder>>(json, options);
                if (reminders != null)
                {
                    foreach (var r in reminders)
                    {
                        try
                        {
                            var checkCmd = new SqlCommand("SELECT COUNT(*) FROM FinancialReminders WHERE Id = @id", conn);
                            checkCmd.Parameters.AddWithValue("@id", r.Id);
                            int exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                            if (exists == 0)
                            {
                                var insSql = @"SET IDENTITY_INSERT FinancialReminders ON;
                                               INSERT INTO FinancialReminders (Id, Title, [Type], Category, ReminderType, GeneralExpenseId, DebtId, Amount, MinPayment, AccruedInterest, TaxAmount, PrincipalPaid, CurrentTotalDebt, InterestStartDate, DayOfMonth, DueDate, IsRecurringMonthly, Status, IsPaid, PaidDate, PaidFrom, BankAccountId, InvoiceNo, RemainingDebtAfter, Description, IsDeleted)
                                               VALUES (@id, @t, @rtype, @c, @rtype, @geid, @did, @amt, @min, @aint, @tax, @ppaid, @ctot, @isdate, @dom, @ddate, @rec, @stat, @ispaid, @pdate, @pfrom, @baid, @invno, @rem, @desc, @del);
                                               SET IDENTITY_INSERT FinancialReminders OFF;";
                                var cmd = new SqlCommand(insSql, conn);
                                cmd.Parameters.AddWithValue("@id", r.Id);
                                cmd.Parameters.AddWithValue("@t", r.Title ?? "");
                                cmd.Parameters.AddWithValue("@c", r.Category ?? "Genel");
                                cmd.Parameters.AddWithValue("@rtype", r.ReminderType ?? "Genel Gider");
                                cmd.Parameters.AddWithValue("@ispaid", r.Status == "Ödendi");
                                cmd.Parameters.AddWithValue("@geid", (object?)r.GeneralExpenseId ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@did", (object?)r.DebtId ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@amt", r.Amount);
                                cmd.Parameters.AddWithValue("@min", (object?)r.MinPayment ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@aint", (object?)r.AccruedInterest ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@tax", (object?)r.TaxAmount ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@ppaid", (object?)r.PrincipalPaid ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@ctot", (object?)r.CurrentTotalDebt ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@isdate", (object?)r.InterestStartDate ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@dom", r.DayOfMonth);
                                cmd.Parameters.AddWithValue("@ddate", r.DueDate);
                                cmd.Parameters.AddWithValue("@rec", r.IsRecurringMonthly);
                                cmd.Parameters.AddWithValue("@stat", r.Status ?? "Bekliyor");
                                cmd.Parameters.AddWithValue("@pdate", (object?)r.PaidDate ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@pfrom", (object?)r.PaidFrom ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@baid", (object?)r.BankAccountId ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@invno", (object?)r.InvoiceNo ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@rem", (object?)r.RemainingDebtAfter ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@desc", (object?)r.Description ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@del", r.IsDeleted);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }
    }
}
