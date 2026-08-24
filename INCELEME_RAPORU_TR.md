# NetworkScanner_CSharp Teknik İnceleme Raporu

**İncelenen sürüm:** 1.3.0.0  
**Proje türü:** Windows Forms, .NET Framework 4.8  
**İnceleme kapsamı:** Proje yapısı, tarama algoritması, dış süreç çağrıları, güncelleme mekanizması, port tarama akışı, P/Invoke kullanımı ve temel statik kontroller.

## Genel değerlendirme

Proje, yerel ağdaki cihazları keşfetmek için iyi bir temel oluşturuyor. Paralel ping taraması, MAC/vendor/hostname zenginleştirmesi, port tarama, cihaz türü tahmini, CSV dışa aktarma ve Türkçe-İngilizce arayüz gibi işlevler tek bir masaüstü uygulamasında birleştirilmiş. Bununla birlikte, ana uygulama mantığının neredeyse tamamının **5.700 satırı aşan tek bir `MainForm.cs` dosyasında** toplanması bakım maliyetini yükseltiyor. En önemli iyileştirmeler güvenli süreç başlatma, güncelleme doğrulaması, tarama kapsamının sınırlandırılması, iptal/timeout yönetimi ve test edilebilir mimari alanlarında.

## Öncelikli bulgular

| Öncelik | Bulgu | Etki | Önerilen aksiyon |
|---|---|---|---|
| Kritik | SSH kullanıcı adı `cmd.exe` komutuna doğrudan ekleniyor | Özel karakter içeren kullanıcı adı komut satırı davranışını bozabilir; kötüye kullanım riski doğurur | `ProcessStartInfo.ArgumentList` kullanın veya kullanıcı adını sıkı bir regex ile doğrulayın; mümkünse `ssh.exe` doğrudan başlatılsın |
| Yüksek | Otomatik güncelleme yalnızca `MZ` başlığını kontrol ediyor | İndirilen EXE’nin gerçekten beklenen yayıncıya ait olduğu doğrulanmıyor | Authenticode imzası veya yayıncı sertifikası doğrulaması ve SHA-256 manifest kontrolü ekleyin |
| Yüksek | Kullanıcı aralığı için üst sınır görünmüyor | `/0`, çok geniş aralık veya hatalı giriş milyonlarca görev ve UI baskısı oluşturabilir | En fazla hedef sayısı belirleyin; örneğin varsayılan 65.536, gelişmiş modda açık onay isteyin |
| Yüksek | Cihaz başına 23 port testi, her aktif cihaz için paralel başlatılıyor | Büyük ağlarda soket, CPU ve ağ yükü hızla büyür; ağ cihazlarında gürültü oluşturur | Aşamalı tarama, global `SemaphoreSlim`, seçilebilir port profilleri ve rate limit uygulayın |
| Orta | `NetApiBufferFreeHost` P/Invoke bildirimi şüpheli | Standart Windows API adı `NetApiBufferFree` olduğundan SMB bilgisi alınırken çalışma zamanı hatası oluşabilir | `EntryPoint = "NetApiBufferFree"` kullanın ve P/Invoke test ekleyin |
| Orta | Çok sayıda boş `catch` bloğu ve genel `catch (Exception)` bulunuyor | Gerçek hatalar gözlemlenemiyor; hata ayıklama ve kullanıcı desteği zorlaşıyor | Yapısal loglama, hata kodları ve hedefli exception yakalama ekleyin |
| Orta | `arp.exe` çağrısında süreç yaşam döngüsü için açık timeout yok | Komut takılırsa arka planda süreç kalabilir | Ortak timeout’lu süreç yardımcısı kullanın; stdout/stderr birlikte tüketilsin |
| Düşük | Sürüm bilgisi `.csproj` içindeki `ApplicationVersion` ile AssemblyInfo arasında tutarlı görünmüyor | Paketleme ve güncelleme karşılaştırmalarında sürüm karışıklığı yaratabilir | Tek bir sürüm kaynağı kullanın ve CI derlemesinde otomatik üretin |

## 1. Güvenlik incelemesi

### SSH ve komut satırı çağrıları

`MainForm.cs` içindeki SSH akışında kullanıcı adı, `cmd.exe` komutunun içine string birleştirme ile ekleniyor. Kullanıcı adı yalnızca boş olup olmadığı açısından kontrol ediliyor. Bu yaklaşım yerine shell katmanını tamamen kaldırmak daha güvenli olur:

```csharp
var psi = new ProcessStartInfo
{
    FileName = sshPath,
    UseShellExecute = false,
    CreateNoWindow = false
};

psi.ArgumentList.Add($"{username}@{selectedIp}");
Process.Start(psi);
```

.NET Framework 4.8 hedefinde `ArgumentList` desteği bulunmadığı proje ayarlarına göre garanti edilemiyorsa, kullanıcı adını `^[A-Za-z0-9._-]{1,64}$` benzeri bir kuralla doğrulayıp güvenli bir quoting yardımcı metodu yazın. `selectedIp` de ayrıca `IPAddress.TryParse` ile doğrulanmalıdır. Aynı prensip `RunCmd`, RDP, Explorer ve URL açma akışlarında da uygulanmalıdır.

`RunCmd("ping " + selectedIp)` ve `RunCmd("tracert " + selectedIp)` mevcut akışta keşfedilen IP adreslerinden beslendiği için risk SSH kullanıcı adı kadar doğrudan görünmüyor; ancak `RunCmd` genel bir yardımcı olduğu için shell komutu kabul eden tasarım gereksiz derecede tehlikelidir. Bunun yerine her işlem için ayrı bir `RunProcessAsync(fileName, arguments, timeout, cancellationToken)` yardımcı sınıfı oluşturulmalıdır.

### Otomatik güncelleme

Güncelleme mekanizması HTTPS üzerinden GitHub release bilgisini alıyor ve indirilen dosyanın yalnızca ilk iki byte’ının `MZ` olup olmadığını kontrol ediyor. Bu kontrol dosyanın PE biçiminde olduğunu gösterebilir, fakat bütünlük veya yayıncı doğrulaması sağlamaz. Güncelleme akışında aşağıdaki sıra önerilir:

1. Release metadata içindeki sürüm, asset adı, beklenen SHA-256 ve imza bilgisi alınmalı.
2. İndirme boyutu için makul bir üst sınır ve `Content-Length` doğrulaması uygulanmalı.
3. Dosyanın SHA-256 özeti manifest ile karşılaştırılmalı.
4. Windows Authenticode imzası ve beklenen yayıncı sertifikası doğrulanmalı.
5. Başarısız doğrulamada dosya çalıştırılmadan silinmeli.
6. Güncelleme işlemi ayrı ve minimal bir updater bileşenine taşınmalı.

Geçici `.cmd` ile mevcut EXE’nin üzerine yazılması çalışabilir; ancak hata durumunda geri dönüş mekanizması yoktur. Daha sağlam çözüm, eski EXE’yi `.bak` olarak saklamak, yeni dosyayı atomik biçimde değiştirmek ve başlatma başarısız olursa geri almaktır.

## 2. Performans ve ölçeklenebilirlik

Ana tarama döngüsü `SemaphoreSlim` ile aynı anda 64 hedefi sınırlandırıyor. Bu iyi bir başlangıç olsa da cihaz bulunduğunda `CompleteDeviceDetailsAsync` ayrıca hostname, vendor ve yaklaşık 23 TCP port testini başlatıyor. Böylece üst seviye tarama limiti korunurken cihaz ayrıntısı tarafında çok daha yüksek sayıda soket işlemi oluşabiliyor. Örneğin 100 aktif cihaz için 2.300 port bağlantısı kısa sürede başlatılabilir.

Önerilen aşamalı tasarım şöyledir:

| Aşama | Varsayılan davranış | Kontrol |
|---|---|---|
| Keşif | ICMP veya ARP ile aktif hedefleri bul | 32–64 eşzamanlı görev |
| Hafif zenginleştirme | MAC, hostname ve temel servisler | 8–16 eşzamanlı görev |
| Ayrıntılı port taraması | Kullanıcının seçtiği profil | Global rate limit |

Kullanıcıya **Hızlı**, **Standart** ve **Ayrıntılı** tarama profilleri sunulması hem performansı hem de kullanım deneyimini iyileştirir. Ayrıca `CancellationToken` yalnızca ana ping taramasında değil, hostname çözümleme, TCP bağlantıları ve dış süreçlerde de kullanılmalıdır.

`BuildRange` metodu başlangıç ve bitiş arasında bulunan her adresi üretir. Bu metot teknik olarak doğru olsa da kullanıcı girdisine göre çok büyük bir aralık oluşabilir. Aralık hesaplandıktan hemen sonra sayısal hedef sayısı kontrol edilmeli ve güvenli bir limit aşılırsa tarama başlamadan uyarı gösterilmelidir. IPv4 `/0` gibi aralıklar varsayılan olarak reddedilmelidir.

## 3. Doğruluk ve hata yönetimi

Kodda çok sayıda boş `catch` bloğu bulunuyor. Bu durum tek bir cihazın hatasının tüm taramayı durdurmamasını sağlar; ancak hangi işlemin, hangi hedefte ve hangi nedenle başarısız olduğu kaybolur. En azından aşağıdaki bağlamlar loglanmalıdır:

```text
Timestamp, operation, target, exception type, elapsed time, cancellation state
```

Kullanıcı arayüzünde teknik exception mesajı doğrudan gösterilmek yerine kullanıcıya anlaşılır bir mesaj gösterilmeli; ayrıntı log dosyasına yazılmalıdır. Log seviyesi `Debug`, `Info`, `Warning`, `Error` olarak ayrılabilir. Harici bir paket eklemek istenmezse küçük bir `AppLogger` sınıfı yeterlidir.

SMB bölümündeki `NetApiBufferFreeHost` bildirimi özellikle test edilmelidir. Windows API’nin standart işlev adı `NetApiBufferFree` olduğundan mevcut C# metot adı DLL export adıyla eşleşmiyorsa `EntryPointNotFoundException` oluşabilir. Güvenli bildirim şu biçimde olmalıdır:

```csharp
[DllImport("Netapi32.dll", EntryPoint = "NetApiBufferFree")]
private static extern int NetApiBufferFree(IntPtr buffer);
```

P/Invoke kodu için Windows üzerinde küçük bir entegrasyon testi eklenmesi önerilir. `Marshal.PtrToStructure` ve unmanaged buffer temizleme işlemleri de bu testte doğrulanmalıdır.

## 4. Mimari ve bakım kolaylığı

`MainForm.cs` içinde UI yerleşimi, tarama orkestrasyonu, Windows API çağrıları, hostname çözümleme, vendor tahmini, port tarama, güncelleme, CSV export ve süreç başlatma aynı sınıfta bulunuyor. Bu yapı yeni özellik eklemeyi ve regresyon testlerini zorlaştırır. Aşağıdaki ayrıştırma uygun olur:

| Sınıf veya katman | Sorumluluk |
|---|---|
| `NetworkScanService` | Hedef üretimi, ping/ARP keşfi ve iptal yönetimi |
| `PortScanner` | TCP bağlantı testleri, timeout ve eşzamanlılık |
| `HostnameResolver` | DNS, NetBIOS, mDNS ve Windows yardımcıları |
| `DeviceClassifier` | Vendor, hostname ve port verisinden cihaz türü tahmini |
| `WindowsNetworkInterop` | `SendARP`, NetAPI ve diğer P/Invoke bildirimleri |
| `ProcessRunner` | Güvenli argüman aktarımı, timeout, stdout/stderr ve exit code |
| `UpdateService` | Sürüm kontrolü, indirme, bütünlük/imza doğrulama ve rollback |
| `MainForm` | Yalnızca UI durumu ve kullanıcı etkileşimi |

Bu ayrım sonrasında `DeviceInfo`, `PortResult` ve tarama sonuçları UI’dan bağımsız modeller olarak tutulabilir. Böylece cihaz sınıflandırması ve IP aralığı üretimi Windows Forms olmadan birim test edilebilir.

## 5. Test planı

Projede görünür bir test projesi bulunmuyor. İlk aşamada ağ erişimi gerektirmeyen saf fonksiyonlar için test eklenmelidir: `BuildRange`, IP sıralaması, MAC biçimlendirme, CSV quoting, sürüm ayrıştırma, hostname temizleme ve cihaz türü kuralları. Sonraki aşamada mock tabanlı port tarama ve süreç çalıştırma testleri eklenebilir.

Önerilen minimum test senaryoları şunlardır:

| Alan | Test senaryosu |
|---|---|
| IP aralığı | Tek IP, ters aralık, ağ/geçit adresleri, en büyük izinli aralık, limit aşımı |
| CSV | Virgül, tırnak, satır sonu ve Unicode içeren alanlar |
| SSH | Boş, geçersiz ve shell karakterleri içeren kullanıcı adları |
| Port tarama | Timeout, bağlantı reddi, iptal ve eşzamanlılık limiti |
| Güncelleme | Geçersiz EXE, yanlış hash, imza yok, ağ timeout’u ve rollback |
| P/Invoke | SMB bilgisi bulunamadı, unmanaged buffer temizleme ve API hata kodu |
| UI yaşam döngüsü | Form kapanırken devam eden tarama ve `BeginInvoke` sonrası dispose durumu |

## Önerilen uygulama sırası

İlk sprintte SSH ve tüm dış süreç çağrıları güvenli hale getirilmeli, hedef aralığına sınır konulmalı ve güncelleme dosyası hash/imza ile doğrulanmalıdır. İkinci sprintte port taraması global eşzamanlılık limiti ve iptal desteğiyle yeniden düzenlenmelidir. Üçüncü sprintte `MainForm.cs` servis sınıflarına bölünmeli, loglama eklenmeli ve saf iş kuralları için test projesi oluşturulmalıdır. Son olarak güncelleme rollback’i, tarama profilleri ve ağ keşfinde IPv6 desteği gibi işlevsel iyileştirmeler eklenebilir.

## Sonuç

Proje işlev bakımından zengin ve kullanıcıya değer sağlayan bir noktada. En yüksek getiriyi sağlayacak değişiklikler yeni özellik eklemekten önce **güvenli süreç çalıştırma**, **güvenilir güncelleme**, **kontrollü eşzamanlılık**, **geniş aralık koruması** ve **servis katmanlarına ayrıştırma** olacaktır. Bu adımlar uygulandığında uygulama hem büyük ağlarda daha kararlı çalışır hem de sonraki özelliklerin eklenmesi önemli ölçüde kolaylaşır.

## Kaynaklar

[1]: file:///home/ubuntu/work/NetworkScanner_CSharp/MainForm.cs "NetworkScanner_CSharp MainForm.cs"
[2]: file:///home/ubuntu/work/NetworkScanner_CSharp/NetworkScannerTool.csproj "NetworkScanner_CSharp proje dosyası"
[3]: file:///home/ubuntu/work/NetworkScanner_CSharp/Properties/AssemblyInfo.cs "NetworkScanner_CSharp AssemblyInfo.cs"
[4]: file:///home/ubuntu/work/NetworkScanner_CSharp/README.md "NetworkScanner_CSharp README.md"
