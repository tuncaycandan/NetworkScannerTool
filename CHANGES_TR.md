# Uygulanan değişiklikler

SSH ve dış süreç çağrıları özellikle değiştirilmemiştir. Diğer rapor maddeleri kapsamında IP aralığına 65.536 hedef sınırı, cihaz ayrıntısı taramalarına 16 görevlik global limit, güncelleme dosyası için Authenticode imza varlığı kontrolü, `NetApiBufferFree` P/Invoke düzeltmesi ve yerel loglama altyapısı eklenmiştir.

Derleme Windows/.NET Framework 4.8 ortamında yapılmalıdır. Dijital imza kontrolü imzalı release EXE gerektirir; imzasız geliştirme build’leri otomatik güncelleme kurulumu sırasında reddedilecektir.

## Yeni arayüz düzenlemeleri

Güncellemeyi Denetle butonu kaldırılmış, sol alt bölüme `v1.3` biçiminde tıklanabilir mevcut sürüm bağlantısı eklenmiştir. Bu bağlantıya tıklanması güncelleme kontrolünü başlatır. CSV Dışa Aktar butonunun yanına eklenen `Log` butonu, `%LOCALAPPDATA%\NetworkScannerTool` klasörünü Windows Explorer’da açar.


## Son düzeltmeler

Log sistemi artık uygulama başlangıcını, arayüz sürümünü, ağ taraması başlangıç/bitiş/iptal durumlarını, bulunan cihazları, port taraması başlangıç/bitiş durumlarını, güncelleme kontrollerini, CSV dışa aktarmayı ve hataları kaydeder. Uygulama açıldığında `%LOCALAPPDATA%\NetworkScannerTool\network-scanner.log` dosyası oluşturulur.

Log butonu, formun 980 piksel istemci genişliği içine alınmış ve sağ kenara hizalanmıştır. Butonun tıklanması log klasörünü Windows Explorer’da açar.


## Son arayüz düzeltmeleri

`Tüm ağları tara` checkbox’ı görünür işaret alanı ve hizalı metin ayarlarıyla yeniden düzenlenmiştir. Başlangıç IP alanından Tab ile çıkıldığında, geçerli IPv4 adresinin son okteti otomatik olarak `254` yapılır ve bitiş IP alanına yazılır. Örneğin `10.0.0.1` değeri `10.0.0.254` olarak tamamlanır.


## HTML rapor geliştirmeleri

HTML raporuna cihaz türü ve cihaz durumu için gerçek tarama sonuçlarından oluşturulan pasta diyagramları, renkli lejantlar ve özet kartları eklenmiştir. Rapor açıldığında cihaz türü checkbox filtreleriyle yalnızca seçilen türler görüntülenebilir ve yazdırılabilir; filtre alanı yazdırma sırasında otomatik gizlenir. İngilizce arayüzde tarama butonu metni `Scan` olarak kısaltılmıştır.


## Logo entegrasyonu

Kullanıcının seçtiği NetworkScanner logosu `Resources\\tuncay_gokturk.png` kaynağına ve `ico.ico` uygulama ikonuna entegre edilmiştir. About penceresi aynı kaynak görseli kullandığı için yeni logo orada da otomatik görünür.


## N ikon entegrasyonu

Yeni N logolu `networkscanner_N_logo.ico` uygulamanın `ApplicationIcon` ayarına entegre edilmiştir. Şeffaf N logosu `Resources\\networkscanner_about.png` kaynağına bağlanmış ve About penceresinde kullanılmaktadır. Sağ alt bölümdeki eski `tuncay_gokturk.png` imzası korunmuştur.


## About penceresi tasarımı

About/Hakkında penceresi modern başlık paneli, N logolu ürün alanı, dinamik sürüm bilgisi, uygulama açıklaması, düzenli geliştirici bilgisi, tıklanabilir GitHub/web bağlantıları ve stilize Tamam düğmesiyle yeniden düzenlenmiştir.
