# NetworkScannerTool – Proje İncelemesi

## Genel değerlendirme

Proje, Windows Forms üzerinde çalışan ve yerel ağ keşfi, port taraması, hostname/MAC/vendor çözümleme, paylaşım keşfi ve yardımcı ağ araçlarını tek uygulamada birleştiren işlevsel bir araçtır. Mevcut Release derlemesi başarıyla tamamlanmıştır. Buna karşılık, ana form dosyasında çok sayıda sorumluluğun toplanması, dış süreç çağrılarının dağınık olması ve görünür bir test projesinin bulunmaması bakım maliyetini artırmaktadır.

## Bulgular ve öncelikler

| Öncelik | Bulgum | Etki | Önerilen çözüm |
|---|---|---|---|
| P0 | SSH ve dış süreç çağrıları kullanıcı girdileriyle oluşturuluyor. | Komut enjeksiyonu ve hatalı argüman riski. | `ProcessStartInfo.ArgumentList` veya güvenli argüman yardımcı sınıfı kullanın; `cmd /k` kullanımını kaldırın. |
| P0 | Güncelleme/indirme akışında bütünlük ve imza doğrulaması net değil. | Bozuk veya değiştirilmiş çalıştırılabilir dosya riski. | HTTPS, SHA-256 ve mümkünse Authenticode imza doğrulaması; başarısızlıkta rollback. |
| P1 | Tarama, port kontrolü, DNS/NetBIOS/SMB ve UI orkestrasyonu `MainForm.cs` içinde. | Regresyon ve yeni özellik ekleme zorluğu. | `NetworkScanService`, `PortScanner`, `HostnameResolver`, `WindowsNetworkInterop`, `ProcessRunner` sınıflarına ayırın. |
| P1 | Port taramasında farklı akışların eşzamanlılık politikası dağınık görünüyor. | Büyük aralıklarda kaynak tüketimi ve ağ yükü. | Global `SemaphoreSlim`, iptal token’ı ve sabit timeout politikası uygulayın. |
| P1 | Çok sayıda boş `catch` bloğu bulunuyor. | Hatalar görünmez oluyor; teşhis zorlaşıyor. | En azından operasyon, hedef, exception türü ve süreyi loglayın; kullanıcıya teknik ayrıntı göstermeyin. |
| P1 | Görünür birim/integrasyon test projesi yok. | Kritik iş kuralları regresyona açık. | Önce saf fonksiyonlar için testler: IP aralığı, MAC biçimi, CSV quoting, hostname temizleme ve cihaz sınıflandırma. |
| P2 | Modeller alan (field) tabanlı ve UI’dan bağımsız sözleşme zayıf. | Veri akışı ve test edilebilirlik sınırlı. | Salt okunur özellikler, nullability sözleşmesi ve sonuç DTO’ları kullanın. |
| P2 | IPv4 ağırlıklı keşif ve aralık üretimi mevcut. | IPv6-only veya karma ağlarda kapsam eksikliği. | IPv6 keşfini ayrı bir özellik olarak planlayın; IPv4 davranışını koruyan testler ekleyin. |

## Önerilen uygulama sırası

İlk değişiklik olarak güvenli süreç çalıştırma katmanı ve merkezi loglama ele alınmalıdır. İkinci adımda tarama iptali ile eşzamanlılık sınırı ortaklaştırılmalıdır. Üçüncü adımda `MainForm` servis katmanlarına ayrılmalı ve saf iş kuralları test projesine taşınmalıdır. Güncelleme doğrulaması ve rollback bu temel güvenlik adımlarının hemen ardından tamamlanmalıdır.

## Doğrulama

`dotnet msbuild NetworkScannerTool.csproj /t:Build /p:Configuration=Release` komutu ile Release derlemesi başarıyla tamamlanmıştır. Proje klasik .NET Framework 4.8 ve Windows Forms hedeflemektedir; bu nedenle çalışma zamanı doğrulaması Windows üzerinde yapılmalıdır.

## Sonraki adım

Kod değişikliğine başlamadan önce ilk sprint kapsamının seçilmesi önerilir. En düşük riskli ve en yüksek getirili başlangıç, `ProcessRunner` sınıfının eklenmesi, SSH/`cmd.exe` çağrılarının bu sınıfa taşınması ve ilgili giriş değerleri için test yazılmasıdır.
