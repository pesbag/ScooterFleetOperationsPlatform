from pathlib import Path
from io import BytesIO
from pypdf import PdfReader,PdfWriter
from reportlab.pdfgen import canvas
from PIL import Image,ImageDraw
p=Path('output/project-book');r=PdfReader(p/'book.pdf');w=PdfWriter();w.clone_document_from_reader(r)
for i,page in enumerate(w.pages):
 b=BytesIO();c=canvas.Canvas(b,pagesize=(float(page.mediabox.width),float(page.mediabox.height)));c.setFillColorRGB(.04,.32,.28);c.setFont('Helvetica',9);c.drawCentredString(float(page.mediabox.width)/2,25,f'{i+1} / {len(w.pages)}');c.save();page.merge_page(PdfReader(b).pages[0])
with (p/'Bike-Fleet-Project-Book.pdf').open('wb') as f:w.write(f)
fs=sorted(p.glob('thumb-*.jpg')); sheet=Image.new('RGB',(1200,((len(fs)+7)//8)*225),'#dce5e2');d=ImageDraw.Draw(sheet)
for i,f in enumerate(fs):
 im=Image.open(f);im.thumbnail((140,200));x=(i%8)*150;y=(i//8)*225;sheet.paste(im,(x,y));d.text((x+5,y+203),str(i+1),fill='black')
sheet.save(p/'page-review.jpg');print('Verified PDF pages:',len(PdfReader(p/'Bike-Fleet-Project-Book.pdf').pages),'thumbnails:',len(fs))
