from pathlib import Path

import pypdfium2 as pdfium
from PIL import Image, ImageDraw


root = Path(__file__).resolve().parents[1]
pdf_path = root / "output" / "pdf" / "Hadron-ERP-System-User-Handbook-FA.pdf"
out = root / "tmp" / "pdfs" / "rendered"
out.mkdir(parents=True, exist_ok=True)

pdf = pdfium.PdfDocument(str(pdf_path))
thumbs = []
for index, page in enumerate(pdf):
    image = page.render(scale=1.15).to_pil()
    image.save(out / f"page-{index + 1:03}.png")
    thumb = image.copy()
    thumb.thumbnail((320, 226))
    thumbs.append(thumb)

for start in range(0, len(thumbs), 20):
    batch = thumbs[start:start + 20]
    sheet = Image.new("RGB", (4 * 340, 5 * 250), "#d9d9d9")
    draw = ImageDraw.Draw(sheet)
    for offset, image in enumerate(batch):
        x = (offset % 4) * 340 + 10
        y = (offset // 4) * 250 + 10
        sheet.paste(image, (x, y + 14))
        draw.text((x, y), str(start + offset + 1), fill="black")
    sheet.save(out / f"contact-{start // 20 + 1:02}.png")

print(f"rendered={len(thumbs)} contacts={(len(thumbs) + 19) // 20}")
