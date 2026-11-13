using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using banthietbidientu.Data;
using banthietbidientu.Models;
using banthietbidientu.Services;
using System;

namespace banthietbidientu.Controllers
{
    public class ProductManagerController : BaseAdminController
    {
        public ProductManagerController(ApplicationDbContext context, IEmailSender emailSender) : base(context, emailSender)
        {
        }

        public IActionResult Index()    
        {
            return View("~/Views/Admin/QuanLySanPham.cshtml", _context.SanPhams.AsNoTracking().ToList());
        }

        [Authorize(Roles = "Boss")]
        [HttpGet]
        public IActionResult ThemSanPham()
        {
            return View("~/Views/Admin/ThemSanPham.cshtml");
        }

        [Authorize(Roles = "Boss")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ThemSanPham(SanPham model, int slHaNoi, int slDaNang, int slHCM)
        {
            ModelState.Remove("GioHangs");
            ModelState.Remove("ChiTietDonHangs");
            ModelState.Remove("DanhGias");
            ModelState.Remove("ChiTietPhieuNhaps");

            if (slHaNoi < 0 || slDaNang < 0 || slHCM < 0)
            {
                ModelState.AddModelError("", "Số lượng tồn kho các chi nhánh không được âm!");
            }

            if (_context.SanPhams.Any(p => p.Name.ToLower() == (model.Name ?? "").ToLower().Trim()))
            {
                ModelState.AddModelError("Name", "Tên sản phẩm này đã tồn tại trong hệ thống!");
            }

            if (ModelState.IsValid)
            {
                using (var transaction = _context.Database.BeginTransaction())
                {
                    try
                    {
                        model.Name = model.Name.Trim();
                        model.SoLuong = slHaNoi + slDaNang + slHCM;
                        if (string.IsNullOrEmpty(model.MoTa)) model.MoTa = model.Description ?? "";
                        if (string.IsNullOrEmpty(model.Description)) model.Description = model.MoTa ?? "";

                        _context.SanPhams.Add(model);
                        _context.SaveChanges(); // Lưu lần 1 để lấy ID

                        // 2. Lưu kho hàng chi nhánh
                        var k1 = new KhoHang { SanPhamId = model.Id, StoreId = 1, SoLuong = slHaNoi };
                        var k2 = new KhoHang { SanPhamId = model.Id, StoreId = 2, SoLuong = slDaNang };
                        var k3 = new KhoHang { SanPhamId = model.Id, StoreId = 3, SoLuong = slHCM };

                        _context.KhoHangs.AddRange(k1, k2, k3);
                        _context.SaveChanges(); // Lưu lần 2

                        transaction.Commit(); // Chốt giao dịch

                        GhiNhatKy("Thêm sản phẩm", $"Thêm mới: {model.Name} (Tổng tồn: {model.SoLuong})");
                        TempData["Success"] = $"Thêm sản phẩm '{model.Name}' thành công!";
                        return RedirectToAction("Index");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        string err = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                        TempData["Error"] = $"Lỗi khi lưu sản phẩm: {err}";
                    }
                }
            }
            else
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                TempData["Error"] = "Dữ liệu không hợp lệ: " + string.Join(", ", errors);
            }
            return View("~/Views/Admin/ThemSanPham.cshtml", model);
        }

        [Authorize(Roles = "Boss")]
        [HttpGet]
        public IActionResult SuaSanPham(int id)
        {
            var sp = _context.SanPhams.Find(id);
            if (sp == null) return NotFound();

            var khoHangs = _context.KhoHangs.Where(k => k.SanPhamId == id).ToList();

            ViewBag.SlHaNoi = khoHangs.FirstOrDefault(k => k.StoreId == 1)?.SoLuong ?? 0;
            ViewBag.SlDaNang = khoHangs.FirstOrDefault(k => k.StoreId == 2)?.SoLuong ?? 0;
            ViewBag.SlHCM = khoHangs.FirstOrDefault(k => k.StoreId == 3)?.SoLuong ?? 0;

            return View("~/Views/Admin/SuaSanPham.cshtml", sp);
        }

        [Authorize(Roles = "Boss")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SuaSanPham(SanPham model, int slHaNoi, int slDaNang, int slHCM)
        {
            ModelState.Remove("GioHangs");
            ModelState.Remove("ChiTietDonHangs");
            ModelState.Remove("DanhGias");
            ModelState.Remove("ChiTietPhieuNhaps");

            if (slHaNoi < 0 || slDaNang < 0 || slHCM < 0)
            {
                ModelState.AddModelError("", "Số lượng tồn kho không được âm!");
            }

            if (_context.SanPhams.Any(p => p.Id != model.Id && p.Name.ToLower() == (model.Name ?? "").ToLower().Trim()))
            {
                ModelState.AddModelError("Name", "Tên sản phẩm này đã được sử dụng bởi sản phẩm khác!");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var sp = _context.SanPhams.Find(model.Id);
                    if (sp != null)
                    {
                        // Cập nhật thông tin cơ bản
                        sp.Name = model.Name.Trim();
                        sp.Price = model.Price;
                        sp.GiaNhap = model.GiaNhap;
                        sp.Category = model.Category;
                        sp.ImageUrl = model.ImageUrl;
                        sp.Description = model.Description ?? "";
                        sp.MoTa = model.MoTa ?? model.Description ?? "";
                        sp.SoLuong = slHaNoi + slDaNang + slHCM;

                        // Cập nhật Kho
                        var khoHangs = _context.KhoHangs.Where(k => k.SanPhamId == sp.Id).ToList();

                        void UpdateStock(int storeId, int qty)
                        {
                            var kho = khoHangs.FirstOrDefault(k => k.StoreId == storeId);
                            if (kho != null) kho.SoLuong = qty;
                            else _context.KhoHangs.Add(new KhoHang { SanPhamId = sp.Id, StoreId = storeId, SoLuong = qty });
                        }

                        UpdateStock(1, slHaNoi);
                        UpdateStock(2, slDaNang);
                        UpdateStock(3, slHCM);

                        _context.SaveChanges();

                        GhiNhatKy("Sửa sản phẩm", $"Cập nhật sản phẩm: {sp.Name} (ID: {sp.Id})");
                        TempData["Success"] = $"Cập nhật sản phẩm '{sp.Name}' thành công!";
                        return RedirectToAction("Index");
                    }
                    else
                    {
                        TempData["Error"] = "Không tìm thấy sản phẩm!";
                    }
                }
                catch (Exception ex)
                {
                    string err = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    TempData["Error"] = $"Lỗi khi cập nhật sản phẩm: {err}";
                }
            }
            else
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                TempData["Error"] = "Dữ liệu không hợp lệ: " + string.Join(", ", errors);
            }

            ViewBag.SlHaNoi = slHaNoi;
            ViewBag.SlDaNang = slDaNang;
            ViewBag.SlHCM = slHCM;
            return View("~/Views/Admin/SuaSanPham.cshtml", model);
        }

        [Authorize(Roles = "Boss")]
        [HttpPost]
        public IActionResult XoaSanPham(int id)
        {
            try
            {
                var sp = _context.SanPhams.Find(id);
                if (sp != null)
                {
                    // Kiểm tra ràng buộc đơn hàng lịch sử
                    bool coDonHang = _context.ChiTietDonHangs.Any(ct => ct.SanPhamId == id) || _context.LichSuMuaHangs.Any(l => l.ProductId == id);
                    if (coDonHang)
                    {
                        TempData["Error"] = $"Không thể xóa sản phẩm '{sp.Name}' vì đã có lịch sử đơn hàng của khách. Bạn có thể chuyển số lượng về 0 để ngừng bán!";
                        return RedirectToAction("Index");
                    }

                    // Kiểm tra ràng buộc phiếu nhập hàng và chuyển kho
                    bool coPhieuNhap = _context.ChiTietPhieuNhaps.Any(ct => ct.SanPhamId == id) || _context.LichSuNhapHangs.Any(l => l.SanPhamId == id);
                    bool coChuyenKho = _context.PhieuChuyenKhos.Any(p => p.SanPhamId == id);
                    if (coPhieuNhap || coChuyenKho)
                    {
                        TempData["Error"] = $"Không thể xóa sản phẩm '{sp.Name}' vì đã có lịch sử nhập kho hoặc điều phối luân chuyển. Bạn có thể cập nhật số lượng về 0 để ngừng bán!";
                        return RedirectToAction("Index");
                    }

                    // Xóa giỏ hàng tạm thời của khách chứa sản phẩm này
                    var gioHangs = _context.GioHangs.Where(g => g.ProductId == id);
                    _context.GioHangs.RemoveRange(gioHangs);

                    // Xóa đánh giá liên quan
                    var danhGias = _context.DanhGias.Where(d => d.SanPhamId == id);
                    _context.DanhGias.RemoveRange(danhGias);

                    // Xóa tồn kho các chi nhánh
                    var khoHangs = _context.KhoHangs.Where(k => k.SanPhamId == id);
                    _context.KhoHangs.RemoveRange(khoHangs);

                    string tenSp = sp.Name;
                    _context.SanPhams.Remove(sp);
                    _context.SaveChanges();

                    GhiNhatKy("Xóa sản phẩm", $"Đã xóa sản phẩm: {tenSp}");
                    TempData["Success"] = $"Đã xóa sản phẩm '{tenSp}' thành công!";
                }
            }
            catch (Exception ex)
            {
                string err = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                TempData["Error"] = $"Lỗi khi xóa sản phẩm: {err}";
            }
            return RedirectToAction("Index");
        }

        [Authorize(Roles = "Boss")]
        [HttpPost]
        public async Task<IActionResult> CapNhatGiaNhapNhanh(int id, decimal giaNhap)
        {
            try
            {
                var sp = await _context.SanPhams.FindAsync(id);
                if (sp != null && giaNhap >= 0)
                {
                    sp.GiaNhap = giaNhap;
                    await _context.SaveChangesAsync();
                    GhiNhatKy("Cập nhật giá vốn", $"Cập nhật nhanh giá vốn SP {id}: {giaNhap:N0}");
                    return Json(new { success = true });
                }
                return Json(new { success = false, message = "Dữ liệu không hợp lệ" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}