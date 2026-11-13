using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Text; // Dùng cho xuất Excel
using System.Collections.Generic;
using banthietbidientu.Data;
using banthietbidientu.Models;
using banthietbidientu.Services;

namespace banthietbidientu.Controllers
{
    public class WarehouseController : BaseAdminController
    {
        public WarehouseController(ApplicationDbContext context, IEmailSender emailSender) : base(context, emailSender)
        {
        }

        // ==========================================================
        // 1. QUẢN LÝ NHẬP HÀNG (Boss)
        // ==========================================================

        [Authorize(Roles = "Boss")]
        public IActionResult QuanLyNhapHang()
        {
            var list = _context.PhieuNhaps
                .Include(p => p.ChiTiets).ThenInclude(ct => ct.SanPham)
                .OrderByDescending(p => p.NgayNhap)
                .ToList();
            return View("~/Views/Admin/QuanLyNhapHang.cshtml", list);
        }

        [Authorize(Roles = "Boss")]
        [HttpGet]
        public IActionResult NhapHang()
        {
            // Lấy danh sách sản phẩm để hiển thị trong dropdown
            ViewBag.SanPhams = _context.SanPhams
                .Where(p => !string.IsNullOrEmpty(p.Name))
                .OrderBy(p => p.Name)
                .ToList();
            return View("~/Views/Admin/NhapHang.cshtml");
        }

        [Authorize(Roles = "Boss")]
        [HttpPost]
        public IActionResult XuLyNhapHang(int sanPhamId, decimal giaNhap, int slHaNoi, int slDaNang, int slHCM)
        {
            // Tính tổng số lượng nhập từ 3 kho
            int tongSoLuongNhap = slHaNoi + slDaNang + slHCM;

            // Validation cơ bản
            if (tongSoLuongNhap <= 0 || giaNhap < 0)
            {
                TempData["Error"] = "Vui lòng nhập số lượng ít nhất cho 1 chi nhánh và giá nhập hợp lệ!";
                return RedirectToAction("NhapHang");
            }

            var sanPham = _context.SanPhams.Find(sanPhamId);
            if (sanPham == null) return NotFound();

            using (var transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    // A. TÍNH GIÁ VỐN BÌNH QUÂN GIA QUYỀN (Weighted Average Cost)
                    // Công thức: (Giá trị cũ + Giá trị nhập mới) / Tổng số lượng mới
                    decimal tongGiaTriCu = (decimal)sanPham.SoLuong * sanPham.GiaNhap;
                    decimal tongGiaTriNhap = (decimal)tongSoLuongNhap * giaNhap;
                    int tongSoLuongMoi = sanPham.SoLuong + tongSoLuongNhap;

                    if (tongSoLuongMoi > 0)
                    {
                        sanPham.GiaNhap = (tongGiaTriCu + tongGiaTriNhap) / tongSoLuongMoi;
                    }

                    // Cập nhật tổng tồn kho của sản phẩm (Hiển thị chung)
                    sanPham.SoLuong += tongSoLuongNhap;

                    // B. PHÂN BỔ HÀNG VÀO TỪNG KHO (Bảng KhoHang)
                    void AddStock(int storeId, int qty)
                    {
                        if (qty > 0)
                        {
                            var kho = _context.KhoHangs.FirstOrDefault(k => k.SanPhamId == sanPhamId && k.StoreId == storeId);
                            if (kho != null)
                            {
                                kho.SoLuong += qty;
                            }
                            else
                            {
                                _context.KhoHangs.Add(new KhoHang { SanPhamId = sanPhamId, StoreId = storeId, SoLuong = qty });
                            }
                        }
                    }

                    AddStock(1, slHaNoi);   // Kho Hà Nội
                    AddStock(2, slDaNang);  // Kho Đà Nẵng
                    AddStock(3, slHCM);     // Kho TP.HCM

                    // C. LƯU PHIẾU NHẬP
                    // Tự động tạo ghi chú phân bổ để lưu vết
                    string autoNote = $"Phân bổ: HN({slHaNoi}), ĐN({slDaNang}), HCM({slHCM})";

                    var phieuNhap = new PhieuNhap
                    {
                        NgayNhap = DateTime.Now,
                        GhiChu = autoNote,
                        TongTien = tongGiaTriNhap
                    };
                    _context.PhieuNhaps.Add(phieuNhap);
                    _context.SaveChanges();

                    var chiTiet = new ChiTietPhieuNhap
                    {
                        PhieuNhapId = phieuNhap.Id,
                        SanPhamId = sanPhamId,
                        SoLuongNhap = tongSoLuongNhap,
                        GiaNhap = giaNhap
                    };
                    _context.ChiTietPhieuNhaps.Add(chiTiet);
                    _context.SaveChanges();

                    transaction.Commit();

                    GhiNhatKy("Nhập kho", $"Nhập {tongSoLuongNhap} {sanPham.Name}. {autoNote}. Giá vốn mới: {sanPham.GiaNhap:N0}");
                    TempData["Success"] = "Nhập hàng và phân bổ kho thành công!";
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    TempData["Error"] = "Lỗi nhập hàng: " + ex.Message;
                }
            }

            return RedirectToAction("QuanLyNhapHang");
        }

        [Authorize(Roles = "Boss")]
        [HttpPost]
        public async Task<IActionResult> XoaPhieuNhap(int id)
        {
            var phieuNhap = await _context.PhieuNhaps
                .Include(p => p.ChiTiets)
                .ThenInclude(ct => ct.SanPham)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (phieuNhap == null)
            {
                TempData["Error"] = "Không tìm thấy phiếu nhập!";
                return RedirectToAction("QuanLyNhapHang");
            }

            // Kiểm tra an toàn tồn kho trước khi trừ lại
            foreach (var ct in phieuNhap.ChiTiets)
            {
                var sp = ct.SanPham;
                if (sp != null)
                {
                    var khoHN = await _context.KhoHangs.FirstOrDefaultAsync(k => k.SanPhamId == ct.SanPhamId && k.StoreId == 1);
                    if (sp.SoLuong < ct.SoLuongNhap || (khoHN != null && khoHN.SoLuong < ct.SoLuongNhap))
                    {
                        TempData["Error"] = $"Không thể xóa phiếu nhập #{id} vì sản phẩm '{sp.Name}' đã được bán hoặc luân chuyển (tồn kho hiện tại ít hơn số lượng đã nhập).";
                        return RedirectToAction("QuanLyNhapHang");
                    }
                }
            }

            // Hoàn trả lại số lượng tồn kho
            foreach (var ct in phieuNhap.ChiTiets)
            {
                var sp = ct.SanPham;
                if (sp != null)
                {
                    sp.SoLuong -= ct.SoLuongNhap;
                    var khoHN = await _context.KhoHangs.FirstOrDefaultAsync(k => k.SanPhamId == ct.SanPhamId && k.StoreId == 1);
                    if (khoHN != null)
                    {
                        khoHN.SoLuong -= ct.SoLuongNhap;
                    }
                }
            }

            _context.ChiTietPhieuNhaps.RemoveRange(phieuNhap.ChiTiets);
            _context.PhieuNhaps.Remove(phieuNhap);
            await _context.SaveChangesAsync();

            GhiNhatKy("Xóa phiếu nhập", $"Đã xóa phiếu nhập #{phieuNhap.Id} và hoàn lại số lượng tồn kho.");
            TempData["Success"] = $"Đã xóa phiếu nhập #{phieuNhap.Id} thành công!";
            return RedirectToAction("QuanLyNhapHang");
        }

        // --- CHUYỂN KHO ---
        public IActionResult QuanLyChuyenKho()
        {
            var user = _context.TaiKhoans.AsNoTracking().FirstOrDefault(u => u.Username == User.Identity.Name);
            int? storeId = user?.StoreId;
            bool isBoss = user?.Role == "Boss";

            // Truyền thông tin người dùng xuống View để xử lý ẩn hiện nút bấm
            ViewBag.CurrentStoreId = storeId ?? 0;
            ViewBag.IsBoss = isBoss;

            var query = _context.PhieuChuyenKhos.Include(p => p.SanPham).AsQueryable();

            // Nếu không phải Boss, chỉ thấy phiếu liên quan đến kho của mình (Đi hoặc Đến)
            if (!isBoss && storeId.HasValue)
            {
                query = query.Where(p => p.TuKhoId == storeId || p.DenKhoId == storeId);
            }

            return View("~/Views/Admin/QuanLyChuyenKho.cshtml", query.OrderByDescending(p => p.NgayTao).ToList());
        }

        [HttpGet]
        public IActionResult TaoLenhChuyenKho()
        {
            // Chỉ lấy sản phẩm có hàng
            ViewBag.SanPhams = _context.SanPhams.Where(p => p.SoLuong > 0).ToList();

            var user = _context.TaiKhoans.FirstOrDefault(u => u.Username == User.Identity.Name);
            ViewBag.CurrentStoreId = user?.StoreId;
            ViewBag.IsBoss = user?.Role == "Boss";

            return View("~/Views/Admin/TaoLenhChuyenKho.cshtml");
        }

        [HttpPost]
        public async Task<IActionResult> TaoLenhChuyenKho(PhieuChuyenKho model)
        {
            var user = _context.TaiKhoans.FirstOrDefault(u => u.Username == User.Identity.Name);
            bool isBoss = user?.Role == "Boss";

            // Nếu là Admin kho, tự động gán Kho Đến là kho của họ, Kho Đi = 0 (Chờ duyệt)
            if (!isBoss)
            {
                model.DenKhoId = user.StoreId ?? 0;
                model.TuKhoId = 0;
            }

            if (model.SoLuong <= 0) ModelState.AddModelError("SoLuong", "Số lượng yêu cầu chuyển phải lớn hơn 0!");
            if (isBoss && (model.TuKhoId == 0 || model.DenKhoId == 0)) ModelState.AddModelError("", "Chọn đủ 2 kho!");
            if (model.TuKhoId != 0 && model.TuKhoId == model.DenKhoId) ModelState.AddModelError("", "Kho đi và kho đến không được trùng nhau!");

            if (isBoss && model.TuKhoId != 0 && model.SoLuong > 0)
            {
                var khoNguon = await _context.KhoHangs.FirstOrDefaultAsync(k => k.SanPhamId == model.SanPhamId && k.StoreId == model.TuKhoId);
                if (khoNguon == null || khoNguon.SoLuong < model.SoLuong)
                {
                    ModelState.AddModelError("", $"Kho xuất ({GetStoreName(model.TuKhoId)}) chỉ còn tồn {(khoNguon?.SoLuong ?? 0)} sản phẩm, không đủ để chuyển {model.SoLuong}!");
                }
            }

            if (!ModelState.IsValid)
            {
                ViewBag.SanPhams = _context.SanPhams.Where(p => p.SoLuong > 0).ToList();
                ViewBag.CurrentStoreId = user.StoreId;
                ViewBag.IsBoss = isBoss;
                return View("~/Views/Admin/TaoLenhChuyenKho.cshtml", model);
            }

            model.MaPhieu = "CK" + DateTime.Now.ToString("yyyyMMddHHmm");
            model.NguoiTao = user.Username;
            model.NgayTao = DateTime.Now;
            model.TrangThai = 0; // 0: Chờ xử lý/Chờ duyệt

            _context.PhieuChuyenKhos.Add(model);
            await _context.SaveChangesAsync();

            // Tạo thông báo
            var spName = _context.SanPhams.Find(model.SanPhamId)?.Name ?? "SP";
            if (!isBoss)
                TaoThongBaoHeThong("Yêu cầu điều hàng", $"Kho {GetStoreName(model.DenKhoId)} xin {model.SoLuong} {spName}", model.Id.ToString(), "QuanLyChuyenKho", null);
            else
                TaoThongBaoHeThong("Lệnh điều phối", $"Boss chuyển {model.SoLuong} {spName} sang Kho {GetStoreName(model.DenKhoId)}", model.Id.ToString(), "QuanLyChuyenKho", model.TuKhoId);

            TempData["Success"] = "Tạo lệnh điều phối thành công!";
            return RedirectToAction("QuanLyChuyenKho");
        }

        // --- CÁC API XỬ LÝ TRẠNG THÁI (AJAX) ---

        [HttpPost]
        public async Task<IActionResult> DuyetYeuCau(int id, int tuKhoId)
        {
            if (!User.IsInRole("Boss")) return Json(new { success = false, message = "Bạn không có quyền duyệt!" });

            var phieu = await _context.PhieuChuyenKhos.FindAsync(id);
            if (phieu == null) return Json(new { success = false, message = "Không tìm thấy phiếu" });

            if (tuKhoId == phieu.DenKhoId) return Json(new { success = false, message = "Kho xuất trùng với kho nhập!" });

            phieu.TuKhoId = tuKhoId;
            await _context.SaveChangesAsync();

            // Thông báo cho kho xuất biết để xuất hàng
            TaoThongBaoHeThong("Lệnh xuất hàng", $"Boss đã duyệt yêu cầu #{phieu.MaPhieu}. Vui lòng xuất hàng.", phieu.Id.ToString(), "QuanLyChuyenKho", tuKhoId);

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> DuyetXuatKho(int id)
        {
            var phieu = await _context.PhieuChuyenKhos.Include(p => p.SanPham).FirstOrDefaultAsync(p => p.Id == id);
            if (phieu == null || phieu.TrangThai != 0) return Json(new { success = false, message = "Trạng thái phiếu không hợp lệ" });

            // TRỪ KHO ĐI
            var khoDi = await _context.KhoHangs.FirstOrDefaultAsync(k => k.SanPhamId == phieu.SanPhamId && k.StoreId == phieu.TuKhoId);

            if (khoDi == null || khoDi.SoLuong < phieu.SoLuong)
                return Json(new { success = false, message = $"Kho đi ({GetStoreName(phieu.TuKhoId)}) không đủ hàng tồn kho!" });

            khoDi.SoLuong -= phieu.SoLuong;
            phieu.TrangThai = 1; // Chuyển sang: Đang giao hàng
            await _context.SaveChangesAsync();

            TaoThongBaoHeThong("Hàng đang đến", $"Kho {GetStoreName(phieu.TuKhoId)} đã xuất phiếu {phieu.MaPhieu}. Chuẩn bị nhận hàng.", phieu.Id.ToString(), "QuanLyChuyenKho", phieu.DenKhoId);
            GhiNhatKy("Xuất kho", $"Xuất {phieu.SoLuong} {phieu.SanPham.Name} đi {GetStoreName(phieu.DenKhoId)}");

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> XacNhanNhanHang(int id)
        {
            var phieu = await _context.PhieuChuyenKhos.Include(p => p.SanPham).FirstOrDefaultAsync(p => p.Id == id);
            if (phieu == null || phieu.TrangThai != 1) return Json(new { success = false, message = "Phiếu chưa ở trạng thái đang giao" });

            // CỘNG KHO ĐẾN
            var khoDen = await _context.KhoHangs.FirstOrDefaultAsync(k => k.SanPhamId == phieu.SanPhamId && k.StoreId == phieu.DenKhoId);
            if (khoDen != null)
            {
                khoDen.SoLuong += phieu.SoLuong;
            }
            else
            {
                _context.KhoHangs.Add(new KhoHang { SanPhamId = phieu.SanPhamId, StoreId = phieu.DenKhoId, SoLuong = phieu.SoLuong });
            }

            phieu.TrangThai = 2; // Hoàn tất
            phieu.NgayHoanThanh = DateTime.Now;
            await _context.SaveChangesAsync();

            TaoThongBaoHeThong("Hoàn tất", $"Phiếu {phieu.MaPhieu} đã hoàn thành nhập kho.", phieu.Id.ToString(), "QuanLyChuyenKho", null);
            GhiNhatKy("Nhập kho", $"Nhận điều chuyển {phieu.SoLuong} {phieu.SanPham.Name} từ {GetStoreName(phieu.TuKhoId)}");

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> HuyChuyenKho(int id)
        {
            var phieu = await _context.PhieuChuyenKhos.Include(p => p.SanPham).FirstOrDefaultAsync(p => p.Id == id);
            if (phieu == null) return Json(new { success = false, message = "Không tìm thấy phiếu" });

            if (phieu.TrangThai == 0)
            {
                phieu.TrangThai = -1;
                await _context.SaveChangesAsync();
                GhiNhatKy("Hủy chuyển kho", $"Đã hủy phiếu chuyển kho #{phieu.MaPhieu}");
                return Json(new { success = true });
            }
            else if (phieu.TrangThai == 1)
            {
                // Hoàn lại kho xuất phát
                var khoDi = await _context.KhoHangs.FirstOrDefaultAsync(k => k.SanPhamId == phieu.SanPhamId && k.StoreId == phieu.TuKhoId);
                if (khoDi != null) khoDi.SoLuong += phieu.SoLuong;
                else _context.KhoHangs.Add(new KhoHang { SanPhamId = phieu.SanPhamId, StoreId = phieu.TuKhoId, SoLuong = phieu.SoLuong });

                phieu.TrangThai = -1;
                await _context.SaveChangesAsync();
                GhiNhatKy("Hủy chuyển kho", $"Hủy phiếu #{phieu.MaPhieu} khi đang chuyển và đã hoàn {phieu.SoLuong} {phieu.SanPham?.Name} về {GetStoreName(phieu.TuKhoId)}");
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "Không thể hủy phiếu ở trạng thái này!" });
        }

        [Authorize(Roles = "Boss")]
        [HttpPost]
        public async Task<IActionResult> XoaLenhChuyenKho(int id)
        {
            var phieu = await _context.PhieuChuyenKhos.FindAsync(id);
            if (phieu == null)
            {
                TempData["Error"] = "Không tìm thấy phiếu điều phối!";
                return RedirectToAction("QuanLyChuyenKho");
            }

            if (phieu.TrangThai == 1)
            {
                TempData["Error"] = "Phiếu đang trong quá trình luân chuyển hàng, hãy chọn 'Hủy' trước khi xóa!";
                return RedirectToAction("QuanLyChuyenKho");
            }

            string ma = phieu.MaPhieu;
            _context.PhieuChuyenKhos.Remove(phieu);
            await _context.SaveChangesAsync();

            GhiNhatKy("Xóa phiếu chuyển kho", $"Đã xóa phiếu điều phối: {ma}");
            TempData["Success"] = $"Đã xóa phiếu điều phối #{ma} thành công!";
            return RedirectToAction("QuanLyChuyenKho");
        }

        // Hàm hỗ trợ (Helper)
        private string GetStoreName(int id)
        {
            return id switch { 1 => "Hà Nội", 2 => "Đà Nẵng", 3 => "TP.HCM", _ => "Kho" };
        }
    }
}