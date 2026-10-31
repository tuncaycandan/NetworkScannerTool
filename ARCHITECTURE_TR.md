# NetworkScannerTool Mimari Refactor Notu

Bu refactor, uygulamanın çalışma davranışını değiştirmeden kaynak kodunu daha yönetilebilir parçalara ayırır. Ayrı bir Class Library veya ikinci bir proje oluşturulmamıştır. Tüm kaynak dosyaları `NetworkScannerTool.csproj` içinde derlendiği için Release çıktısı yine tek `NetworkScannerTool.exe` olarak üretilir.

## Yeni dosya yapısı

```text
NetworkScannerTool.csproj
MainForm.cs
AppLogger.cs
Models/
  ScanModels.cs
Infrastructure/
  NetShareEnumerator.cs
```

`Models/ScanModels.cs` içinde `AdapterInfo`, `DeviceInfo`, `PortResult` ve `HistoryEntry` veri sınıfları bulunur. Bu sınıflar arayüz kodundan ayrılmış, ancak aynı `NetworkScannerTool` namespace’i içinde tutulmuştur.

`Infrastructure/NetShareEnumerator.cs` içinde SMB paylaşım keşfi, Windows NetAPI P/Invoke tanımları ve `ShareInfo` modeli bulunur. Böylece Windows API/altyapı kodu `MainForm.cs` içinden ayrılmıştır.

`MainForm.cs` hâlâ arayüz olaylarını ve mevcut uygulama akışını yönetir. Bu aşamada yeni özellik eklenmemiş, yalnızca mevcut kodun dosya sorumlulukları ayrıştırılmıştır.

## Derleme ve dağıtım

Projeyi Visual Studio’da `Release` ve `Any CPU` seçerek derleyebilirsiniz. Sonuç yine:

```text
bin\\Release\\NetworkScannerTool.exe
```

şeklinde tek EXE olacaktır. Uygulamanın çalışması için hedef bilgisayarda .NET Framework 4.8 bulunmalıdır.
