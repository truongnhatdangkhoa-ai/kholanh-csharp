import openpyxl, sys
from openpyxl.styles import Alignment, Font, Border, Side
FN='DejaVu Sans'
def fix(ws):
    for row in ws.iter_rows():
        for c in row:
            if c.value is not None and (c.font is None or c.font.name!=FN): c.font=Font(name=FN,size=10)
thin=Side(style='thin'); B=Border(left=thin,right=thin,top=thin,bottom=thin)
C=Alignment(horizontal='center',vertical='center',wrap_text=True)
L=Alignment(horizontal='left',vertical='center',wrap_text=True)

def lan2(path, variant=0):
    wb=openpyxl.Workbook(); ws=wb.active; ws.title='KQ'
    ws.page_setup.orientation='landscape'; ws.page_setup.fitToWidth=1; ws.page_setup.fitToHeight=0
    ws.sheet_properties.pageSetUpPr.fitToPage=True
    widths=[6,30,8,8,8,8,11,8,8,18,20,14,16]
    for i,w in enumerate(widths): ws.column_dimensions[openpyxl.utils.get_column_letter(i+1)].width=w
    ws['A1']='CÔNG TY CỔ PHẦN THỦY SẢN MẪU'; ws['A1'].font=Font(name=FN,bold=True)
    ws.merge_cells('A3:M3'); ws['A3']='KẾT QUẢ PHÂN TÍCH DƯ LƯỢNG KHÁNG SINH'; ws['A3'].alignment=C; ws['A3'].font=Font(name=FN,bold=True,size=14)
    ws['A4']='Số (No): 245/KS-26'; ws['H4']='Ngày phân tích: 0%d.10.2026'%(1+variant)
    r=6
    hdr=['Mẫu','Tên mẫu','Enro','Cipro','Oxy','Doxy','Sulfonamide','AOZ','CAP','KẾT QUẢ\nLẦN 1','KẾT QUẢ\nLẦN 2','Đặt tính','Ghi chú']
    for i,h in enumerate(hdr):
        c=ws.cell(r,i+1,h); c.alignment=C; c.font=Font(name=FN,bold=True); c.border=B
    mr=['','', '(MRPL=2.0 )','(MRPL=2.0 )','(MRPL=8)','(MRPL=8)','(MRPL=8)','(MRPL=0.5)','(MRPL=0.3)','','','','']
    for i,h in enumerate(mr):
        c=ws.cell(r+1,i+1,h or None); c.alignment=C; c.border=B
    for a in ['A','B','J','K','L','M']: ws.merge_cells(f'{a}{r}:{a}{r+1}')
    r+=2
    rows=[
      ('G','MẪU KIỂM'),
      (1,'MKS chờ 1 ngày 25.09\nLSX: 26.09.041\nLô: 2 (29.09.26)',['ND','ND','ND','ND','ND','ND','ND'],'ĐẠT ASC, NHẬT-A','ĐẠT NHẬT, ASC-A,B, HQ, MỸ','01.N-AS2-H',''),
      (2,'MKS chờ 2 ngày 26.09\nLSX: Cook CGC + 055 + / 026\nLô: 3+5 (08.09.26)',['ND','4.83','10','ND','ND','ND','ND'],'KHÔNG ĐẠT','ĐẠT ASC-B, HQ','41.AS1-H','Cipro: 4.83; Oxy: 10'),
      (3,'MKS chờ 3\nLSX: 26.09.043, TOKYO TRADING',['ND','***','ND','ND','2.26','ND','ND'],'ĐẠT ASC','ĐẠT ASC, NHẬT','23.MKS','Sulfo: 2.26'),
      ('G','THÀNH PHẨM SIZE: 3L'),
      (4,'MKS chờ 4 ngày 27.09\nLô: 1 (30.09.26)',['ND','ND','>97.2','ND','ND','ND','ND'],'KHÔNG ĐẠT','KHÔNG ĐẠT','52.KĐ','Oxy: >97.2'),
      (None,'MKS chờ 5 ngày 27.09\nLô: 4 (30.09.26)',['ND','2.1','ND','ND','ND','ND','ND'],'ĐẠT HQ','ĐẠT ASC-B, HQ, MỸ','45.AS1-H-M','Cipro: 2.1'),
      (5,'MKS chờ 6\nLSX: 26.09.050\nLô: 6 (01.10.26)',['ND','ND','ND','ND','ND','ND','<0.5'],'ĐẠT','ĐẠT NHẬT, ASC','02.N-AS2-H','CAP: <0.5'),
    ]
    if variant: rows=rows[:4]+[(4,'MKS chờ 9\nLô: 7 (02.10.26)',['ND']*7,'ĐẠT','ĐẠT NHẬT','01.N-AS2-H','')]
    for row in rows:
        if row[0]=='G':
            ws.merge_cells(f'A{r}:M{r}'); c=ws.cell(r,1,row[1]); c.alignment=C; c.font=Font(name=FN,bold=True); r+=1; continue
        stt,name,vals,k1,k2,dt,gc=row
        n=name.count('\n')+1
        for col in range(1,14):
            if col!=2: ws.merge_cells(start_row=r,start_column=col,end_row=r+n-1,end_column=col)
        ws.cell(r,1,stt).alignment=C
        for j,l in enumerate(name.split('\n')): ws.cell(r+j,2,l).alignment=L
        for j,v in enumerate(vals): ws.cell(r,3+j,v).alignment=C
        for j,v in enumerate([k1,k2,dt,gc]): ws.cell(r,10+j,v).alignment=C
        for rr in range(r,r+n):
            for col in range(1,14): ws.cell(rr,col).border=B
        r+=n
    r+=1
    ws.cell(r,2,'Kiểm nghiệm viên'); ws.cell(r,10,'Đồng Tháp, ngày 0%d tháng 10 năm 2026'%(1+variant))
    fix(ws); wb.save(path)

def lan1(path, variant=0):
    wb=openpyxl.Workbook(); ws=wb.active; ws.title='Kho'
    ws.page_setup.orientation='landscape'; ws.sheet_properties.pageSetUpPr.fitToPage=True; ws.page_setup.fitToHeight=0
    widths=[5,16,7,8,9,8,8,7,7,7,7,10,7,7,18,13,14,6]
    for i,w in enumerate(widths): ws.column_dimensions[openpyxl.utils.get_column_letter(i+1)].width=w
    ws.merge_cells('A2:R2'); ws['A2']='KẾT QUẢ KIỂM KHÁNG SINH NGUYÊN LIỆU (KHO)'; ws['A2'].alignment=C; ws['A2'].font=Font(name=FN,bold=True,size=14)
    r=4
    hdr=['STT','Code/Mã','Số lô','Số con','Khối\nlượng NL','Pcs/lb','Số\nlượng CTN','Enro','Cipro','Oxy','Doxy','Sulfonamide','AOZ','CAP','Kết Quả','Đặt Tính','Ghi Chú','Ẩm']
    for i,h in enumerate(hdr):
        c=ws.cell(r,i+1,h); c.alignment=C; c.font=Font(name=FN,bold=True); c.border=B
    lod=[None]*7+[2,2,8,8,8,0.5,0.3]+[None]*4
    for i,v in enumerate(lod):
        c=ws.cell(r+1,i+1,v); c.alignment=C; c.border=B
    for i in list(range(1,8))+list(range(15,19)): ws.merge_cells(start_row=r,start_column=i,end_row=r+1,end_column=i)
    r+=2
    data=[
      ('G','Ngày nhập nguyên liệu: 30.06.2026'),
      (1,'PHI X1L1\nGiám Sát','12',[58],['ND']*7,'ĐẠT NHẬT, ASC,\nHQ, MỸ','01.N-AS2-H','','75.17%'),
      (2,'THUẬN PHƯỚC\nX2L1','13',[60],['ND','4.83','10','ND','ND','ND','ND'],'ĐẠT ASC-B, HQ','41.AS1-H','Cipro: 4.83','74.45%'),
      (3,'PHI X4L2','14',[58,102],['ND','ND','ND','ND','2.26','ND','ND'],'ĐẠT ASC','23.MKS','Sulfo: 2.26',''),
      ('G','Ngày nhập nguyên liệu: 01.07.2026'),
      (4,'TRÍ X10L1','2',[45],['ND','ND','>97.2','ND','ND','ND','ND'],'KHÔNG ĐẠT','52.KĐ','Oxy: >97.2',''),
      (5,'AN X3L1','3',[50],['ND','2.1','ND','ND','ND','ND','ND'],'ĐẠT HQ','45.AS1-H-M','',''),
    ]
    if variant: data=data[:4]+[(4,'BÌNH X5L1','8',[40],['ND']*7,'ĐẠT','01.N-AS2-H','','')]
    for row in data:
        if row[0]=='G':
            ws.merge_cells(f'A{r}:R{r}'); c=ws.cell(r,1,row[1]); c.alignment=L; c.font=Font(name=FN,bold=True); r+=1; continue
        stt,code,lot,subs,vals,kq,dt,gc,am=row
        n=len(subs)
        for col in [1,2,3]+list(range(8,19)):
            if n>1: ws.merge_cells(start_row=r,start_column=col,end_row=r+n-1,end_column=col)
        ws.cell(r,1,stt).alignment=C; ws.cell(r,2,code).alignment=C; ws.cell(r,3,int(lot)).alignment=C
        for j,s in enumerate(subs):
            ws.cell(r+j,4,s).alignment=C; ws.cell(r+j,5,120+j).alignment=C; ws.cell(r+j,6,'16/20').alignment=C; ws.cell(r+j,7,10).alignment=C
        for j,v in enumerate(vals): ws.cell(r,8+j,v).alignment=C
        for j,v in enumerate([kq,dt,gc,am]): ws.cell(r,15+j,v or None).alignment=C
        for rr in range(r,r+n):
            ws.row_dimensions[rr].height=30
            for col in range(1,19): ws.cell(rr,col).border=B
        r+=n
    r+=1
    ws.cell(r,2,'Ngày kiểm: 0%d.07.2026'%(1+variant)); ws.cell(r,12,'Đồng Tháp, ngày 0%d tháng 07 năm 2026'%(1+variant))
    fix(ws); wb.save(path)

lan2('lan2-a.xlsx'); lan2('lan2-b.xlsx',1); lan1('lan1-a.xlsx'); lan1('lan1-b.xlsx',1)
