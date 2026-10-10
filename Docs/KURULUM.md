# ERP Starter Kurulum

1. Windows'a .NET 10 SDK ve Visual Studio 2026 (Desktop development with .NET) kurun.
2. SQL Server Express/Developer kurulu olsun.
3. `Database/01_schema.sql` dosyasını SQL Server Management Studio'da çalıştırın.
4. `ERP.sln` dosyasını Visual Studio'da açın.
5. `ERP.Desktop/Services/DatabaseService.cs` içindeki ConnectionString'i SQL Server'ınıza göre değiştirin.
6. NuGet restore yaptıktan sonra F5 ile çalıştırın.
7. Yayın almak için: `dotnet publish -c Release -r win-x64 --self-contained true`.

Not: Bu starter sürümünde arayüz ve veri modeli hazırdır. Gerçek veritabanı CRUD, satış kaydı, stok düşümü, maliyet, fatura ve finans hareketleri sonraki geliştirme adımlarında bağlanacaktır.
