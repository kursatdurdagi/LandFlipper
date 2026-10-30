# Land Flipper — UI sanat yönetimi (v2: çizilmiş tombul stil)

Stilize 3D dünyaya ait, elle çizilmiş oyuncak gibi bir arayüz. Token'lar `UI/Styles/Common.uss` → `:root`.

## Görsel dil
- **Kontur:** her yüzeyin koyu kahve (`--ink`) 2–3 px konturu var. Siyah kullanma.
- **Dudak:** alt kenar daha kalın (buton 4–5 px, panel 8 px), basılabilir 3D his. Basınca dudak incelir, öğe 2 px çöker.
- **Yumuşak köşeler:** 10 (küçük) · 14 (buton) · 20–22 (kart/panel) · tam (rozet, para, hap).
- **Hafif eğim:** başlık etiketleri, fiyatlar, rozetler -5°…+3° eğik; düz ızgarayı kırar. Abartma, tek öğe başına bir eğim.
- **Gradient, cam efekti, neon yok.** Derinlik = kontur + dudak + sert (bulanıksız) metin gölgesi.

## Palet
`--cream` yüzey · `--cream-2/3` iç yüzey · `--ink` kontur/metin · `--leaf` birincil eylem ·
`--sun` para/XP/vurgu · `--tomato` uyarı/puan · `--sky` ayarlar · `--wood` taşıma/ahşap.

## Ekran kimliği = etiket rengi
Her modal kart, üst kenarına yapışık renkli bir **etiketle** (`.sticker`) açılır:
Yetenekler `leaf` · Ayarlar `sky` · Mola `wood` · Alet Tezgâhı (mağaza) `sun` · Co-op `sky`.
Gelecek ekranlar: **Bilgisayar** `sky` ama kart yerine koyu ekranlı tombul bir monitör çerçevesi;
**İnşaat** alt kenarda alçak bir tombul şerit, kategori sekmeleri `sun`, maliyet sarı etiket.

## Hareket
- Hover: 2–4 px yukarı + ≤1.5° eğim, `ease-out-back`.
- Menü girişi: öğeler 70 ms arayla soldan kayar; logo yaylanarak düşer ve yavaşça sallanır.
- HUD: para artınca hap zıplar ve eğilir; seviye kartı yaylanarak açılır.

## Font
Varsayılan font, kalın ağırlık + kontur ile. Lisanslı yuvarlak bir aile eklenirse (öneri: Baloo 2 / Fredoka başlık,
Nunito gövde) `Common.uss` içinde `-unity-font-definition` ile tek yerden değiştirilir.
