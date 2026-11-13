using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using banthietbidientu.Data;
using banthietbidientu.Models;
using banthietbidientu.Services;
using System.Collections.Generic;
using System;
using System.Text;
using System.Threading.Tasks;

namespace banthietbidientu.Controllers
{
    [Authorize(Roles = "Admin,Boss")]
    public class AdminController : BaseAdminController
    {
        // Constructor kế thừa từ BaseAdminController
        public AdminController(ApplicationDbContext context, IEmailSender emailSender) : base(context, emailSender)
        {
        }

        // --- 1. DASHBOARD (THỐNG KÊ) ---
        public IActionResult Index()
        {
            var user = _context.TaiKhoans.AsNoTracking()
                .FirstOrDefault(u => u.Username == User.Identity.Name);

            ViewBag.CurrentStoreId = user?.StoreId ?? 0;
            int? storeId = user?.StoreId;
            var queryDonHang = _context.DonHangs.AsQueryable();

            if (storeId.HasValue)
            {
                queryDonHang = queryDonHang.Where(d => d.StoreId == storeId);
            }

            var today = DateTime.Today;

            ViewBag.DoanhThuHomNay = queryDonHang
                .Where(d => d.TrangThai == 3 && d.NgayDat.Value.Date == today)
                .Sum(x => x.TongTien);

            ViewBag.DonHangHomNay = queryDonHang
                .Count(d => d.NgayDat.Value.Date == today);

            ViewBag.DonChoXuLy = queryDonHang
                .Count(d => d.TrangThai == 0 || d.TrangThai == 1);

            var startOfMonth = new DateTime(today.Year, today.Month, 1);
            var startOfLastMonth = startOfMonth.AddMonths(-1);
            var endOfLastMonth = startOfMonth.AddDays(-1);

            decimal revThisMonth = queryDonHang
                .Where(d => d.TrangThai == 3 && d.NgayDat >= startOfMonth)
                .Sum(x => x.TongTien);

            decimal revLastMonth = queryDonHang
                .Where(d => d.TrangThai == 3 && d.NgayDat >= startOfLastMonth && d.NgayDat <= endOfLastMonth)
                .Sum(x => x.TongTien);

            double growth = 0;
            if (revLastMonth > 0)
            {
                growth = (double)((revThisMonth - revLastMonth) / revLastMonth) * 100;
            }
            else if (revThisMonth > 0)
            {
                growth = 100;
            }

            ViewBag.RevenueThisMonth = revThisMonth;
            ViewBag.RevenueGrowth = growth;
            ViewBag.TongDoanhThu = queryDonHang.Where(d => d.TrangThai == 3).Sum(x => x.TongTien);
            ViewBag.TongDonHang = queryDonHang.Count();
            ViewBag.TongKhachHang = _context.TaiKhoans.Count(x => x.Role == "User");

            var chartLabels = new List<string>();
            var chartRevenue = new List<decimal>();
            var chartOrders = new List<int>();

            for (int i = 6; i >= 0; i--)
            {
                var day = today.AddDays(-i);
                chartLabels.Add(day.ToString("dd/MM"));
                var dailyData = queryDonHang.Where(x => x.NgayDat.Value.Date == day);

                chartRevenue.Add(dailyData.Where(x => x.TrangThai == 3).Sum(x => (decimal?)x.TongTien) ?? 0);
                chartOrders.Add(dailyData.Count());
            }

            ViewBag.ChartLabels = chartLabels;
            ViewBag.ChartRevenue = chartRevenue;
            ViewBag.ChartOrders = chartOrders;

            ViewBag.PieCompleted = queryDonHang.Count(x => x.TrangThai == 3);
            ViewBag.PieShipping = queryDonHang.Count(x => x.TrangThai == 2);
            ViewBag.PiePending = queryDonHang.Count(x => x.TrangThai == 0 || x.TrangThai == 1);
            ViewBag.PieCancelled = queryDonHang.Count(x => x.TrangThai == -1);

            var recentOrders = queryDonHang
                .Include(d => d.TaiKhoan)
                .OrderByDescending(d => d.NgayDat)
                .Take(5)
                .Select(d => new DonHangViewModel
                {
                    MaDon = d.MaDon,
                    TenKhachHang = d.NguoiNhan,
                    NgayDat = d.NgayDat,
                    TongTien = d.TongTien,
                    TrangThai = d.TrangThai == 1 ? "Đã xác nhận" :
                                d.TrangThai == 2 ? "Đang giao" :
                                d.TrangThai == 3 ? "Hoàn thành" :
                                d.TrangThai == -1 ? "Đã hủy" : "Chờ xử lý"
                })
                .ToList();

            return View(recentOrders);
        }

        // --- 2. QUẢN LÝ ĐƠN HÀNG ---
        public IActionResult QuanLyDonHang(int? page)
        {
            int pageSize = 10;
            int pageNumber = page ?? 1;

            var user = _context.TaiKhoans.AsNoTracking()
                .FirstOrDefault(u => u.Username == User.Identity.Name);

            int? storeId = user?.StoreId;

            var query = _context.DonHangs
                .Include(d => d.TaiKhoan)
                .Include(d => d.ChiTietDonHangs)
                    .ThenInclude(ct => ct.SanPham)
                .AsQueryable();

            if (storeId.HasValue)
            {
                query = query.Where(d => d.StoreId == storeId);
            }

            query = query.OrderByDescending(d => d.NgayDat);

            int totalItems = query.Count();
            int totalPages = (int)Math.Ceiling((double)totalItems / pageSize);

            var listDonHang = query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(d => new DonHangViewModel
                {
                    MaDon = d.MaDon,
                    TenKhachHang = d.NguoiNhan,
                    NgayDat = d.NgayDat,
                    TongTien = d.TongTien,
                    TrangThai = d.TrangThai == 1 ? "Đã xác nhận" :
                                d.TrangThai == 2 ? "Đang giao" :
                                d.TrangThai == 3 ? "Hoàn thành" :
                                d.TrangThai == -1 ? "Đã hủy" : "Chờ xử lý",
                    StoreId = d.StoreId,
                    SanPhams = d.ChiTietDonHangs.Select(ct => new DonHangViewModel
                    {
                        TenSanPham = ct.SanPham.Name,
                        HinhAnh = ct.SanPham.ImageUrl,
                        SoLuong = ct.SoLuong,
                        Gia = ct.Gia
                    }).ToList()
                })
                .ToList();

            ViewBag.TotalPages = totalPages;
            ViewBag.CurrentPage = pageNumber;

            return View(listDonHang);
        }

        [HttpPost]
        public async Task<IActionResult> CapNhatTrangThai(string orderId, string status)
        {
            try
            {
                var donHang = await _context.DonHangs
                                      .Include(d => d.TaiKhoan)
                                      .Include(d => d.ChiTietDonHangs)
                                      .FirstOrDefaultAsync(x => x.MaDon == orderId);

                if (donHang == null)
                {
                    return Json(new { success = false, message = $"Không tìm thấy đơn {orderId}" });
                }

                string trangThaiCu = donHang.TrangThai == 1 ? "Đã xác nhận" :
                                     donHang.TrangThai == 2 ? "Đang giao" :
                                     donHang.TrangThai == 3 ? "Hoàn thành" :
                                     donHang.TrangThai == -1 ? "Đã hủy" : "Chờ xử lý";

                int statusInt = 0;
                if (status == "Đã xác nhận") statusInt = 1;
                else if (status == "Đang giao") statusInt = 2;
                else if (status == "Hoàn thành") statusInt = 3;
                else if (status == "Đã hủy") statusInt = -1;

                // LOGIC HOÀN TỒN KHO & VOUCHER KHI HỦY ĐƠN
                if (statusInt == -1 && donHang.TrangThai != -1)
                {
                    foreach (var ct in donHang.ChiTietDonHangs)
                    {
                        var sp = await _context.SanPhams.FindAsync(ct.SanPhamId);
                        if (sp != null)
                        {
                            sp.SoLuong += ct.SoLuong;
                            if (donHang.StoreId.HasValue)
                            {
                                var kho = await _context.KhoHangs.FirstOrDefaultAsync(k => k.SanPhamId == sp.Id && k.StoreId == donHang.StoreId);
                                if (kho != null) kho.SoLuong += ct.SoLuong;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(donHang.MaVoucher))
                    {
                        var v = await _context.Vouchers.FirstOrDefaultAsync(x => x.MaVoucher == donHang.MaVoucher);
                        if (v != null && v.DaDung > 0) v.DaDung--;
                    }
                }
                // LOGIC PHỤC HỒI ĐƠN ĐÃ HỦY -> TRỪ LẠI KHO
                else if (donHang.TrangThai == -1 && statusInt != -1)
                {
                    foreach (var ct in donHang.ChiTietDonHangs)
                    {
                        var sp = await _context.SanPhams.FindAsync(ct.SanPhamId);
                        if (sp != null)
                        {
                            sp.SoLuong -= ct.SoLuong;
                            if (donHang.StoreId.HasValue)
                            {
                                var kho = await _context.KhoHangs.FirstOrDefaultAsync(k => k.SanPhamId == sp.Id && k.StoreId == donHang.StoreId);
                                if (kho != null)
                                {
                                    kho.SoLuong -= ct.SoLuong;
                                    if (kho.SoLuong < 0) kho.SoLuong = 0;
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(donHang.MaVoucher))
                    {
                        var v = await _context.Vouchers.FirstOrDefaultAsync(x => x.MaVoucher == donHang.MaVoucher);
                        if (v != null) v.DaDung++;
                    }
                }

                donHang.TrangThai = statusInt;
                await _context.SaveChangesAsync();

                GhiNhatKy("Cập nhật đơn hàng", $"Đổi trạng thái đơn {orderId} từ '{trangThaiCu}' sang '{status}'");

                if (statusInt == 3)
                {
                    string userEmail = donHang.TaiKhoan?.Email;
                    if (!string.IsNullOrEmpty(userEmail))
                    {
                        string subject = $"[SmartTech] Đơn hàng #{donHang.MaDon} đã giao thành công!";
                        string body = $@"
                            <div style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
                                <h3 style='color: #28a745;'>Cảm ơn bạn đã mua sắm tại SmartTech!</h3>
                                <p>Xin chào <strong>{donHang.NguoiNhan}</strong>,</p>
                                <p>Đơn hàng <strong>#{donHang.MaDon}</strong> của bạn đã được giao thành công.</p>
                                <p>Chúng tôi hy vọng bạn hài lòng với sản phẩm.</p>
                            </div>";

                        try { await _emailSender.SendEmailAsync(userEmail, subject, body); } catch { }
                    }
                }

                return Json(new { success = true, message = "Cập nhật thành công!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi: " + ex.Message });
            }
        }

        [Authorize(Roles = "Boss")]
        [HttpPost]
        public async Task<IActionResult> XoaDonHang(string orderId)
        {
            try
            {
                var donHang = await _context.DonHangs
                    .Include(d => d.ChiTietDonHangs)
                    .FirstOrDefaultAsync(d => d.MaDon == orderId);

                if (donHang == null)
                {
                    TempData["Error"] = "Không tìm thấy đơn hàng cần xóa!";
                    return RedirectToAction("QuanLyDonHang");
                }

                if (donHang.TrangThai != -1 && donHang.TrangThai != 0)
                {
                    TempData["Error"] = "Chỉ có thể xóa đơn hàng ở trạng thái 'Đã hủy' hoặc 'Chờ xử lý'!";
                    return RedirectToAction("QuanLyDonHang");
                }

                // Nếu đang chờ xử lý (chưa hủy), hoàn trả tồn kho trước khi xóa
                if (donHang.TrangThai == 0)
                {
                    foreach (var ct in donHang.ChiTietDonHangs)
                    {
                        var sp = await _context.SanPhams.FindAsync(ct.SanPhamId);
                        if (sp != null)
                        {
                            sp.SoLuong += ct.SoLuong;
                            if (donHang.StoreId.HasValue)
                            {
                                var kho = await _context.KhoHangs.FirstOrDefaultAsync(k => k.SanPhamId == sp.Id && k.StoreId == donHang.StoreId);
                                if (kho != null) kho.SoLuong += ct.SoLuong;
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(donHang.MaVoucher))
                    {
                        var v = await _context.Vouchers.FirstOrDefaultAsync(x => x.MaVoucher == donHang.MaVoucher);
                        if (v != null && v.DaDung > 0) v.DaDung--;
                    }
                }

                _context.ChiTietDonHangs.RemoveRange(donHang.ChiTietDonHangs);
                _context.DonHangs.Remove(donHang);
                await _context.SaveChangesAsync();

                GhiNhatKy("Xóa đơn hàng", $"Đã xóa đơn hàng: {orderId}");
                TempData["Success"] = $"Đã xóa đơn hàng #{orderId} thành công!";
            }
            catch (Exception ex)
            {
                string err = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                TempData["Error"] = $"Lỗi khi xóa đơn hàng: {err}";
            }
            return RedirectToAction("QuanLyDonHang");
        }

        // --- 4. API THÔNG BÁO ---
        [HttpGet]
        public IActionResult GetThongBao()
        {
            try
            {
                var user = _context.TaiKhoans.AsNoTracking()
                    .FirstOrDefault(u => u.Username == User.Identity.Name);

                int? storeId = user?.StoreId;
                bool isBoss = user?.Role == "Boss";

                var query = _context.ThongBaos.AsQueryable();

                if (!isBoss && storeId.HasValue)
                {
                    query = query.Where(t => t.StoreId == storeId);
                }

                var thongBaos = query
                    .OrderByDescending(x => x.NgayTao)
                    .Take(5)
                    .Select(x => new
                    {
                        id = x.Id,
                        tieuDe = x.TieuDe,
                        noiDung = x.NoiDung,
                        ngayTao = x.NgayTao,
                        daDoc = x.DaDoc,
                        loaiThongBao = x.LoaiThongBao,
                        redirectId = x.RedirectId,
                        redirectAction = x.RedirectAction
                    })
                    .ToList();

                var soChuaDoc = query.Count(x => !x.DaDoc);

                return Json(new { success = true, data = thongBaos, unread = soChuaDoc });
            }
            catch
            {
                return Json(new { success = true, data = new List<object>(), unread = 0 });
            }
        }

        [HttpPost]
        public IActionResult MarkAsRead()
        {
            var list = _context.ThongBaos.Where(x => !x.DaDoc).ToList();
            foreach (var item in list) item.DaDoc = true;
            _context.SaveChanges();
            return Json(new { success = true });
        }

        [HttpPost]
        public IActionResult MarkOneRead(int id)
        {
            var tb = _context.ThongBaos.Find(id);
            if (tb != null)
            {
                tb.DaDoc = true;
                _context.SaveChanges();
                return Json(new { success = true });
            }
            return Json(new { success = false });
        }

        public IActionResult QuanLyThongBao()
        {
            var list = _context.ThongBaos.OrderByDescending(x => x.NgayTao).ToList();
            return View(list);
        }

        [HttpPost]
        public IActionResult XoaThongBao(int id)
        {
            var tb = _context.ThongBaos.Find(id);
            if (tb != null)
            {
                _context.ThongBaos.Remove(tb);
                _context.SaveChanges();
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "Không tìm thấy thông báo" });
        }

        [HttpPost]
        public IActionResult XoaTatCaThongBaoDaDoc()
        {
            var list = _context.ThongBaos.Where(x => x.DaDoc).ToList();
            if (list.Any())
            {
                _context.ThongBaos.RemoveRange(list);
                _context.SaveChanges();
                TempData["Success"] = $"Đã dọn dẹp {list.Count} thông báo đã đọc!";
            }
            else
            {
                TempData["Success"] = "Không có thông báo đã đọc nào cần dọn dẹp.";
            }
            return RedirectToAction("QuanLyThongBao");
        }

        // --- 6. QUẢN LÝ TÀI KHOẢN ---
        [Authorize(Roles = "Boss")]
        public IActionResult QuanLyTaiKhoan()
        {
            return View(_context.TaiKhoans.AsNoTracking().ToList());
        }

        [Authorize(Roles = "Boss")]
        [HttpGet]
        public IActionResult ThemTaiKhoan()
        {
            return View();
        }

        [Authorize(Roles = "Boss")]
        [HttpPost]
        public IActionResult ThemTaiKhoan(TaiKhoan model)
        {
            ModelState.Remove("DonHangs");
            ModelState.Remove("DanhGias");
            ModelState.Remove("YeuCauThuMuas");

            if (!string.IsNullOrEmpty(model.Username) && _context.TaiKhoans.Any(u => u.Username.ToLower() == model.Username.ToLower().Trim()))
            {
                ModelState.AddModelError("Username", "Tên đăng nhập này đã tồn tại trong hệ thống!");
            }

            if (!string.IsNullOrEmpty(model.Email) && _context.TaiKhoans.Any(u => u.Email.ToLower() == model.Email.ToLower().Trim()))
            {
                ModelState.AddModelError("Email", "Địa chỉ email này đã được sử dụng!");
            }

            if (model.Role != "Admin")
            {
                model.StoreId = null;
            }

            if (ModelState.IsValid)
            {
                try
                {
                    model.Username = model.Username.Trim();
                    model.Email = model.Email.Trim();
                    _context.TaiKhoans.Add(model);
                    _context.SaveChanges();

                    GhiNhatKy("Thêm tài khoản", $"Tạo mới tài khoản: {model.Username} ({model.Role})");
                    TempData["Success"] = $"Tạo tài khoản '{model.Username}' thành công!";
                    return RedirectToAction("QuanLyTaiKhoan");
                }
                catch (Exception ex)
                {
                    string err = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    TempData["Error"] = $"Lỗi khi tạo tài khoản: {err}";
                }
            }
            return View(model);
        }

        [Authorize(Roles = "Boss")]
        [HttpGet]
        public IActionResult SuaTaiKhoan(int id)
        {
            var tk = _context.TaiKhoans.Find(id);
            return tk == null ? NotFound() : View(tk);
        }

        [Authorize(Roles = "Boss")]
        [HttpPost]
        public IActionResult SuaTaiKhoan(TaiKhoan model)
        {
            ModelState.Remove("Password");
            ModelState.Remove("DonHangs");
            ModelState.Remove("DanhGias");
            ModelState.Remove("YeuCauThuMuas");

            if (!string.IsNullOrEmpty(model.Email) && _context.TaiKhoans.Any(u => u.Id != model.Id && u.Email.ToLower() == model.Email.ToLower().Trim()))
            {
                ModelState.AddModelError("Email", "Email này đã được sử dụng bởi tài khoản khác!");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var tk = _context.TaiKhoans.Find(model.Id);
                    if (tk != null)
                    {
                        tk.FullName = model.FullName;
                        tk.Email = model.Email?.Trim();
                        if (tk.Role != "Boss" || User.IsInRole("Boss"))
                        {
                            tk.Role = model.Role;
                        }
                        tk.Address = model.Address;
                        tk.PhoneNumber = model.PhoneNumber;
                        tk.Gender = model.Gender;
                        if (model.DateOfBirth.HasValue) tk.DateOfBirth = model.DateOfBirth;
                        tk.StoreId = model.Role == "Admin" ? model.StoreId : null;

                        if (!string.IsNullOrWhiteSpace(model.Password))
                        {
                            tk.Password = model.Password;
                        }

                        _context.SaveChanges();
                        GhiNhatKy("Sửa tài khoản", $"Cập nhật tài khoản: {tk.Username}");
                        TempData["Success"] = $"Cập nhật tài khoản '{tk.Username}' thành công!";
                        return RedirectToAction("QuanLyTaiKhoan");
                    }
                    else
                    {
                        TempData["Error"] = "Không tìm thấy tài khoản!";
                    }
                }
                catch (Exception ex)
                {
                    string err = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    TempData["Error"] = $"Lỗi khi cập nhật tài khoản: {err}";
                }
            }
            return View(model);
        }

        [Authorize(Roles = "Boss")]
        [HttpPost]
        public IActionResult XoaTaiKhoan(int id)
        {
            try
            {
                var tk = _context.TaiKhoans.Find(id);
                if (tk != null)
                {
                    if (tk.Role == "Boss")
                    {
                        TempData["Error"] = "Không thể xóa tài khoản Boss (Chủ tịch)!";
                        return RedirectToAction("QuanLyTaiKhoan");
                    }

                    if (tk.Username == User.Identity.Name)
                    {
                        TempData["Error"] = "Bạn không thể tự xóa tài khoản đang đăng nhập!";
                        return RedirectToAction("QuanLyTaiKhoan");
                    }

                    if (_context.DonHangs.Any(d => d.TaiKhoanId == id))
                    {
                        TempData["Error"] = $"Tài khoản '{tk.Username}' đã có lịch sử đơn hàng, không thể xóa để bảo toàn dữ liệu!";
                        return RedirectToAction("QuanLyTaiKhoan");
                    }

                    var gioHangs = _context.GioHangs.Where(g => g.UserId == id);
                    _context.GioHangs.RemoveRange(gioHangs);

                    var danhGias = _context.DanhGias.Where(d => d.TaiKhoanId == id);
                    _context.DanhGias.RemoveRange(danhGias);

                    var cauHinhLuong = _context.CauHinhLuongs.Where(c => c.TaiKhoanId == id);
                    _context.CauHinhLuongs.RemoveRange(cauHinhLuong);

                    string tenUser = tk.Username;
                    _context.TaiKhoans.Remove(tk);
                    _context.SaveChanges();

                    GhiNhatKy("Xóa tài khoản", $"Đã xóa tài khoản: {tenUser}");
                    TempData["Success"] = $"Đã xóa tài khoản '{tenUser}' thành công!";
                }
                else
                {
                    TempData["Error"] = "Không tìm thấy tài khoản cần xóa!";
                }
            }
            catch (Exception ex)
            {
                string err = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                TempData["Error"] = $"Lỗi khi xóa tài khoản: {err}";
            }
            return RedirectToAction("QuanLyTaiKhoan");
        }

        // --- 7. BÁO CÁO ---
        [Authorize(Roles = "Boss")]
        public IActionResult BaoCao(int? storeId, int? month, int? year)
        {
            var now = DateTime.Now;
            int selectedYear = year ?? now.Year;
            int selectedMonth = month ?? now.Month;
            int prevMonth = selectedMonth == 1 ? 12 : selectedMonth - 1;
            int prevYearOfMonth = selectedMonth == 1 ? selectedYear - 1 : selectedYear;

            string storeName = "Toàn hệ thống";
            if (storeId == 1) storeName = "Chi nhánh Hà Nội";
            else if (storeId == 2) storeName = "Chi nhánh Đà Nẵng";
            else if (storeId == 3) storeName = "Chi nhánh TP.HCM";

            List<decimal> GetMonthlyData(int y)
            {
                var data = new List<decimal>();
                for (int m = 1; m <= 12; m++)
                {
                    var monthData = _context.DonHangs
                        .Where(d => d.TrangThai == 3 && d.NgayDat.Value.Year == y && d.NgayDat.Value.Month == m && (!storeId.HasValue || d.StoreId == storeId))
                        .Select(d => new { d.TongTien, d.TienThue }).ToList();
                    decimal netRevenue = monthData.Sum(x => x.TongTien - x.TienThue);
                    data.Add(netRevenue);
                }
                return data;
            }

            double CalcGrowth(decimal cur, decimal prev) => prev > 0 ? (double)((cur - prev) / prev) * 100 : 100;

            (decimal DoanhThu, int DonHang, int SanPham, decimal LoiNhuan) CalculateKpi(int m, int y)
            {
                var queryDH = _context.DonHangs.Where(d => d.TrangThai == 3 && d.NgayDat.Value.Year == y && d.NgayDat.Value.Month == m && (!storeId.HasValue || d.StoreId == storeId));
                var queryCT = _context.ChiTietDonHangs.Where(ct => ct.DonHang.TrangThai == 3 && ct.DonHang.NgayDat.Value.Year == y && ct.DonHang.NgayDat.Value.Month == m && (!storeId.HasValue || ct.DonHang.StoreId == storeId));

                decimal tongThu = queryDH.Sum(d => d.TongTien);
                decimal tongThue = queryDH.Sum(d => d.TienThue);
                decimal thucThu = tongThu - tongThue;
                decimal giaVon = queryCT.Sum(ct => ct.GiaGoc * ct.SoLuong);
                return (thucThu, queryDH.Count(), queryCT.Sum(ct => ct.SoLuong), thucThu - giaVon);
            }

            var current = CalculateKpi(selectedMonth, selectedYear);
            var previous = CalculateKpi(prevMonth, prevYearOfMonth);

            var categoryData = _context.ChiTietDonHangs.Include(ct => ct.SanPham)
                .Where(ct => ct.DonHang.TrangThai == 3 && ct.DonHang.NgayDat.Value.Year == selectedYear && ct.DonHang.NgayDat.Value.Month == selectedMonth && (!storeId.HasValue || ct.DonHang.StoreId == storeId))
                .GroupBy(ct => ct.SanPham.Category)
                .Select(g => new { Name = g.Key, Value = g.Sum(x => (decimal?)x.Gia * x.SoLuong) ?? 0 })
                .OrderByDescending(x => x.Value).ToList();

            var storeComparison = new List<decimal>();
            if (!storeId.HasValue)
            {
                for (int i = 1; i <= 3; i++)
                {
                    var storeData = _context.DonHangs.Where(d => d.TrangThai == 3 && d.NgayDat.Value.Year == selectedYear && d.NgayDat.Value.Month == selectedMonth && d.StoreId == i).Select(d => new { d.TongTien, d.TienThue }).ToList();
                    storeComparison.Add(storeData.Sum(x => x.TongTien - x.TienThue));
                }
            }

            var queryChiTietFull = _context.ChiTietDonHangs.Include(ct => ct.SanPham).Where(ct => ct.DonHang.TrangThai == 3 && ct.DonHang.NgayDat.Value.Year == selectedYear && ct.DonHang.NgayDat.Value.Month == selectedMonth && (!storeId.HasValue || ct.DonHang.StoreId == storeId));
            decimal tongDoanhThuNiemYet = queryChiTietFull.Sum(ct => (ct.Gia ?? 0) * ct.SoLuong);
            decimal tyLeThucThu = tongDoanhThuNiemYet > 0 ? (current.DoanhThu / tongDoanhThuNiemYet) : 1;

            var baoCaoLoiNhuan = queryChiTietFull.GroupBy(ct => new { ct.SanPhamId, ct.SanPham.Name, ct.SanPham.ImageUrl })
                .Select(g => new { Ten = g.Key.Name, Hinh = g.Key.ImageUrl, Sl = g.Sum(x => x.SoLuong), DtNiemYet = g.Sum(x => (x.Gia ?? 0) * x.SoLuong), Gv = g.Sum(x => x.GiaGoc * x.SoLuong) }).ToList()
                .Select(x => new LoiNhuanSanPham { TenSanPham = x.Ten, HinhAnh = x.Hinh, SoLuongBan = x.Sl, DoanhThuNiemYet = x.DtNiemYet, GiaVon = x.Gv, DoanhThuThuc = x.DtNiemYet * tyLeThucThu, LoiNhuan = (x.DtNiemYet * tyLeThucThu) - x.Gv })
                .OrderByDescending(x => x.DoanhThuThuc).ToList();

            var vipUsers = _context.TaiKhoans.Where(u => u.Role == "User").Select(u => new { User = u, TotalSpent = u.DonHangs.Where(d => d.TrangThai == 3).Sum(d => (decimal?)d.TongTien - d.TienThue) ?? 0, OrderCount = u.DonHangs.Count(d => d.TrangThai == 3) }).Where(x => x.TotalSpent >= 100000000).OrderByDescending(x => x.TotalSpent).Take(10).Select(x => new KhachHangTiemNang { Id = x.User.Id, HoTen = x.User.FullName, Username = x.User.Username, SoDienThoai = x.User.PhoneNumber, TongChiTieu = x.TotalSpent, SoDonHang = x.OrderCount }).ToList();

            var model = new BaoCaoViewModel { TongDoanhThu = current.DoanhThu, TongDoanhThuGoc = tongDoanhThuNiemYet, TongDonHang = current.DonHang, TongSanPhamDaBan = current.SanPham, LoiNhuanUocTinh = current.LoiNhuan, SanPhamSapHet = _context.SanPhams.Count(s => s.SoLuong < 5), GrowthDoanhThu = CalcGrowth(current.DoanhThu, previous.DoanhThu), GrowthLoiNhuan = CalcGrowth(current.LoiNhuan, previous.LoiNhuan), GrowthDonHang = CalcGrowth(current.DonHang, previous.DonHang), GrowthSanPham = CalcGrowth(current.SanPham, previous.SanPham), DataNamHienTai = GetMonthlyData(selectedYear), DataNamTruoc = GetMonthlyData(selectedYear - 1), DataNamKia = GetMonthlyData(selectedYear - 2), CurrentYear = selectedYear, CategoryLabels = categoryData.Select(x => x.Name).ToList(), CategoryValues = categoryData.Select(x => x.Value).ToList(), StoreRevenueComparison = storeComparison, SelectedStoreId = storeId, StoreName = storeName, BaoCaoLoiNhuan = baoCaoLoiNhuan, KhachHangTiemNangs = vipUsers };
            ViewBag.SelectedMonth = selectedMonth; ViewBag.SelectedYear = selectedYear; ViewBag.IsBoss = true;
            return View(model);
        }

        // --- 8. XUẤT EXCEL & IN HÓA ĐƠN ---
        [HttpGet]
        public IActionResult XuatExcelDonHang()
        {
            var orders = _context.DonHangs.Include(d => d.TaiKhoan).OrderByDescending(d => d.NgayDat).ToList();
            var sb = new StringBuilder();
            sb.AppendLine("Mã đơn hàng,Ngày đặt,Khách hàng,Số điện thoại,Địa chỉ giao hàng,Tổng tiền,Trạng thái");
            foreach (var item in orders)
            {
                string trangThaiText = item.TrangThai switch { 0 => "Chờ xử lý", 1 => "Đã xác nhận", 2 => "Đang giao", 3 => "Hoàn thành", -1 => "Đã hủy", _ => "Khác" };
                string diaChiSafe = item.DiaChi?.Replace("\"", "\"\"") ?? "";
                string ngayDatStr = item.NgayDat.HasValue ? item.NgayDat.Value.ToString("dd/MM/yyyy HH:mm") : "";
                sb.AppendLine($"{item.MaDon},{ngayDatStr},{item.NguoiNhan},{item.SDT},\"{diaChiSafe}\",{item.TongTien},{trangThaiText}");
            }
            var fileContent = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(fileContent, "text/csv", $"BaoCaoDonHang_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        }

        [HttpGet]
        public IActionResult InHoaDon(string orderId)
        {
            var order = _context.DonHangs.Include(d => d.ChiTietDonHangs).ThenInclude(ct => ct.SanPham).Include(d => d.TaiKhoan).FirstOrDefault(d => d.MaDon == orderId);
            if (order == null) return NotFound();
            string shopAddress = "120 Xuân Thủy, Cầu Giấy, Hà Nội";
            string zoneCode = "HN-CG-01";
            string addr = (order.DiaChi ?? "").ToLower();
            string[] mienTrung = { "đà nẵng", "huế", "quảng nam", "quảng ngãi", "bình định", "phú yên", "khánh hòa", "quảng bình", "quảng trị", "nghệ an", "hà tĩnh", "thanh hóa" };
            string[] mienNam = { "hồ chí minh", "tp.hcm", "hcm", "sài gòn", "bình dương", "đồng nai", "bà rịa", "vũng tàu", "long an", "tiền giang", "cần thơ" };
            if (mienNam.Any(k => addr.Contains(k))) { shopAddress = "55 Nguyễn Huệ, Quận 1, TP.HCM"; zoneCode = "SG-Q1-03"; }
            else if (mienTrung.Any(k => addr.Contains(k))) { shopAddress = "78 Bạch Đằng, Hải Châu, Đà Nẵng"; zoneCode = "DN-HC-02"; }
            ViewBag.ShopAddress = shopAddress; ViewBag.ZoneCode = zoneCode;
            var model = new DonHangViewModel { MaDon = order.MaDon, NgayDat = order.NgayDat, TenKhachHang = !string.IsNullOrEmpty(order.NguoiNhan) ? order.NguoiNhan : (order.TaiKhoan?.FullName ?? "Khách lẻ"), SoDienThoai = !string.IsNullOrEmpty(order.SDT) ? order.SDT : (order.TaiKhoan?.PhoneNumber ?? ""), DiaChiGiaoHang = order.DiaChi ?? "", TongTien = order.TongTien, PhuongThucThanhToan = "Thanh toán khi nhận hàng (COD)", SanPhams = order.ChiTietDonHangs.Select(ct => new DonHangViewModel { TenSanPham = ct.SanPham.Name, SoLuong = ct.SoLuong, Gia = ct.Gia }).ToList() };
            return View(model);
        }

        // --- CÁC HÀM KHÁC ---

        public IActionResult QuanLyDanhGia()
        {
            var listDanhGia = _context.DanhGias.Include(d => d.SanPham).Include(d => d.TaiKhoan).OrderByDescending(d => d.NgayTao).ToList();
            return View(listDanhGia);
        }

        [HttpPost]
        public IActionResult DuyetDanhGia(int id)
        {
            var d = _context.DanhGias.Find(id);
            if (d != null)
            {
                d.DaDuyet = !d.DaDuyet;
                _context.SaveChanges();
                GhiNhatKy("Duyệt đánh giá", $"Thay đổi trạng thái duyệt cho đánh giá ID {id}");
                return Json(new { success = true });
            }
            return Json(new { success = false });
        }

        [HttpPost]
        public IActionResult TraLoiDanhGia(int id, string noiDungTraLoi)
        {
            var d = _context.DanhGias.Find(id);
            if (d != null)
            {
                d.TraLoi = noiDungTraLoi;
                d.NgayTraLoi = DateTime.Now;
                _context.SaveChanges();
                GhiNhatKy("Trả lời đánh giá", $"Đã trả lời đánh giá ID {id} của khách hàng");
                return RedirectToAction("QuanLyDanhGia");
            }
            return RedirectToAction("QuanLyDanhGia");
        }

        [HttpPost]
        public IActionResult XoaDanhGia(int id)
        {
            var d = _context.DanhGias.Find(id);
            if (d != null)
            {
                _context.DanhGias.Remove(d);
                _context.SaveChanges();
                GhiNhatKy("Xóa đánh giá", $"Đã xóa đánh giá ID {id}");
            }
            return RedirectToAction("QuanLyDanhGia");
        }

        public IActionResult QuanLyThuMua()
        {
            var listYeuCau = _context.YeuCauThuMuas.Include(y => y.TaiKhoan).OrderByDescending(y => y.NgayTao).ToList();
            return View(listYeuCau);
        }

        [HttpPost]
        public IActionResult CapNhatThuMua(int id, int trangThai, string ghiChuAdmin)
        {
            var y = _context.YeuCauThuMuas.Find(id);
            if (y != null)
            {
                y.TrangThai = trangThai;
                y.GhiChu = ghiChuAdmin;
                _context.SaveChanges();
                GhiNhatKy("Cập nhật thu mua", $"Yêu cầu {y.TenMay} (ID: {id}) -> Trạng thái {trangThai}");
                TempData["Success"] = $"Đã cập nhật trạng thái yêu cầu #{y.MaYeuCau}!";
            }
            return RedirectToAction("QuanLyThuMua");
        }

        [HttpPost]
        public IActionResult XoaThuMua(int id)
        {
            try
            {
                var y = _context.YeuCauThuMuas.Find(id);
                if (y != null)
                {
                    string ma = y.MaYeuCau ?? y.TenMay;
                    _context.YeuCauThuMuas.Remove(y);
                    _context.SaveChanges();
                    GhiNhatKy("Xóa yêu cầu thu mua", $"Đã xóa yêu cầu: {ma}");
                    TempData["Success"] = $"Đã xóa yêu cầu thu mua #{ma} thành công!";
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Lỗi khi xóa yêu cầu: " + ex.Message;
            }
            return RedirectToAction("QuanLyThuMua");
        }

        // --- 11. QUẢN LÝ BANNER ---
        [Authorize(Roles = "Boss")]
        public IActionResult QuanLyBanner()
        {
            var banners = _context.Banners.OrderBy(b => b.DisplayOrder).ToList();
            return View(banners);
        }

        [Authorize(Roles = "Boss")]
        [HttpGet]
        public IActionResult ThemBanner()
        {
            return View();
        }

        [Authorize(Roles = "Boss")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ThemBanner(Banner model)
        {
            var maxOrder = _context.Banners.Any() ? _context.Banners.Max(b => b.DisplayOrder) : 0;
            model.DisplayOrder = maxOrder + 1;
            ModelState.Remove("DisplayOrder");
            if (ModelState.IsValid)
            {
                _context.Banners.Add(model);
                _context.SaveChanges();
                GhiNhatKy("Thêm Banner", $"Thêm banner mới: {model.Title}");
                TempData["Success"] = "Thêm banner mới thành công!";
                return RedirectToAction("QuanLyBanner");
            }
            return View(model);
        }

        [Authorize(Roles = "Boss")]
        [HttpGet]
        public IActionResult SuaBanner(int id)
        {
            var banner = _context.Banners.Find(id);
            if (banner == null) return NotFound();
            return View("SuaBanner", banner);
        }

        [Authorize(Roles = "Boss")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SuaBanner(Banner model)
        {
            ModelState.Remove("DisplayOrder");
            if (ModelState.IsValid)
            {
                var existingBanner = _context.Banners.Find(model.Id);
                if (existingBanner != null)
                {
                    existingBanner.ImageUrl = model.ImageUrl;
                    existingBanner.Title = model.Title;
                    existingBanner.Description = model.Description;
                    existingBanner.ButtonText = model.ButtonText;
                    existingBanner.LinkUrl = model.LinkUrl;
                    existingBanner.IsActive = model.IsActive;
                    _context.SaveChanges();
                    GhiNhatKy("Sửa Banner", $"Cập nhật banner ID: {model.Id}");
                    TempData["Success"] = "Cập nhật banner thành công!";
                    return RedirectToAction("QuanLyBanner");
                }
                else return NotFound();
            }
            return View(model);
        }

        [Authorize(Roles = "Boss")]
        [HttpPost]
        public IActionResult XoaBanner(int id)
        {
            var banner = _context.Banners.Find(id);
            if (banner != null)
            {
                string title = banner.Title;
                _context.Banners.Remove(banner);
                _context.SaveChanges();
                var remainingBanners = _context.Banners.OrderBy(b => b.DisplayOrder).ToList();
                for (int i = 0; i < remainingBanners.Count; i++) remainingBanners[i].DisplayOrder = i + 1;
                _context.SaveChanges();
                GhiNhatKy("Xóa Banner", $"Đã xóa banner: {title}");
                TempData["Success"] = "Đã xóa banner.";
            }
            return RedirectToAction("QuanLyBanner");
        }

        // --- 12. QUẢN LÝ VOUCHER ---
        [Authorize(Roles = "Boss")]
        public IActionResult QuanLyVoucher()
        {
            var list = _context.Vouchers.OrderByDescending(v => v.Id).ToList();
            return View(list);
        }

        [Authorize(Roles = "Boss")]
        [HttpGet]
        public IActionResult ThemVoucher()
        {
            return View();
        }

        [Authorize(Roles = "Boss")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ThemVoucher(Voucher model)
        {
            if (!string.IsNullOrEmpty(model.MaVoucher))
            {
                model.MaVoucher = model.MaVoucher.ToUpper().Trim();
            }

            if (_context.Vouchers.Any(v => v.MaVoucher == model.MaVoucher))
            {
                ModelState.AddModelError("MaVoucher", "Mã voucher này đã tồn tại trong hệ thống!");
            }

            if (model.NgayKetThuc <= model.NgayBatDau)
            {
                ModelState.AddModelError("NgayKetThuc", "Ngày kết thúc phải lớn hơn ngày bắt đầu hiệu lực!");
            }

            if (model.LoaiGiamGia == 1 && (model.GiaTri <= 0 || model.GiaTri > 100))
            {
                ModelState.AddModelError("GiaTri", "Mức giảm theo % phải nằm trong khoảng từ 1% đến 100%!");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Vouchers.Add(model);
                    _context.SaveChanges();
                    GhiNhatKy("Thêm Voucher", $"Tạo mã voucher mới: {model.MaVoucher}");
                    TempData["Success"] = $"Đã tạo mã giảm giá '{model.MaVoucher}' thành công!";
                    return RedirectToAction("QuanLyVoucher");
                }
                catch (Exception ex)
                {
                    string err = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    TempData["Error"] = $"Lỗi khi lưu mã giảm giá: {err}";
                }
            }
            return View(model);
        }

        [Authorize(Roles = "Boss")]
        [HttpGet]
        public IActionResult SuaVoucher(int id)
        {
            var v = _context.Vouchers.Find(id);
            return v == null ? NotFound() : View(v);
        }

        [Authorize(Roles = "Boss")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SuaVoucher(Voucher model)
        {
            if (model.NgayKetThuc <= model.NgayBatDau)
            {
                ModelState.AddModelError("NgayKetThuc", "Ngày kết thúc phải lớn hơn ngày bắt đầu hiệu lực!");
            }

            if (model.LoaiGiamGia == 1 && (model.GiaTri <= 0 || model.GiaTri > 100))
            {
                ModelState.AddModelError("GiaTri", "Mức giảm theo % phải nằm trong khoảng từ 1% đến 100%!");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var v = _context.Vouchers.Find(model.Id);
                    if (v != null)
                    {
                        v.TenVoucher = model.TenVoucher;
                        v.LoaiGiamGia = model.LoaiGiamGia;
                        v.GiaTri = model.GiaTri;
                        v.GiamToiDa = model.GiamToiDa;
                        v.DonToiThieu = model.DonToiThieu;
                        v.SoLuong = model.SoLuong;
                        v.NgayBatDau = model.NgayBatDau;
                        v.NgayKetThuc = model.NgayKetThuc;
                        v.IsActive = model.IsActive;
                        _context.SaveChanges();

                        GhiNhatKy("Sửa Voucher", $"Cập nhật voucher: {v.MaVoucher}");
                        TempData["Success"] = $"Cập nhật mã '{v.MaVoucher}' thành công!";
                        return RedirectToAction("QuanLyVoucher");
                    }
                    else
                    {
                        TempData["Error"] = "Không tìm thấy mã giảm giá cần cập nhật!";
                    }
                }
                catch (Exception ex)
                {
                    string err = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    TempData["Error"] = $"Lỗi khi cập nhật mã: {err}";
                }
            }
            return View(model);
        }

        [Authorize(Roles = "Boss")]
        [HttpPost]
        public IActionResult XoaVoucher(int id)
        {
            try
            {
                var v = _context.Vouchers.Find(id);
                if (v != null)
                {
                    bool daDungChoDonHang = _context.DonHangs.Any(d => d.MaVoucher == v.MaVoucher);
                    if (daDungChoDonHang)
                    {
                        // Thay vì xóa cứng làm mất dấu lịch sử đơn, chuyển trạng thái tắt kích hoạt
                        v.IsActive = false;
                        _context.SaveChanges();
                        GhiNhatKy("Khóa Voucher", $"Đã vô hiệu hóa mã voucher đã có đơn hàng: {v.MaVoucher}");
                        TempData["Success"] = $"Mã '{v.MaVoucher}' đã được áp dụng cho đơn hàng nên hệ thống chuyển sang trạng thái 'Ngừng kích hoạt'!";
                        return RedirectToAction("QuanLyVoucher");
                    }

                    string ma = v.MaVoucher;
                    _context.Vouchers.Remove(v);
                    _context.SaveChanges();
                    GhiNhatKy("Xóa Voucher", $"Đã xóa mã: {ma}");
                    TempData["Success"] = $"Đã xóa mã voucher '{ma}' thành công!";
                }
                else
                {
                    TempData["Error"] = "Không tìm thấy mã voucher cần xóa!";
                }
            }
            catch (Exception ex)
            {
                string err = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                TempData["Error"] = $"Lỗi khi xóa mã: {err}";
            }
            return RedirectToAction("QuanLyVoucher");
        }

        // --- 13. NHẬT KÝ HOẠT ĐỘNG ---
        [Authorize(Roles = "Boss")]
        public IActionResult NhatKyHoatDong(int? storeId, int page = 1)
        {
            int pageSize = 20;

            var query = _context.LichSuHoatDongs
                                .Include(l => l.TaiKhoan)
                                .AsQueryable();

            query = query.Where(l => l.TaiKhoan.Role != "Boss");

            if (storeId.HasValue)
            {
                query = query.Where(l => l.StoreId == storeId);
            }

            int totalItems = query.Count();
            int totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
            if (totalPages < 1) totalPages = 1;

            var listLogs = query.OrderByDescending(l => l.ThoiGian)
                                .Skip((page - 1) * pageSize)
                                .Take(pageSize)
                                .ToList();

            ViewBag.SelectedStore = storeId;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;

            return View(listLogs);
        }

        // --- 14. QUẢN LÝ THUẾ ---
        [Authorize(Roles = "Boss")]
        public IActionResult QuanLyThue(int? month, int? year)
        {
            var now = DateTime.Now;
            int selectedYear = year ?? now.Year;
            int selectedMonth = month ?? now.Month;
            var query = _context.DonHangs.AsNoTracking().Where(d => d.TrangThai == 3);
            decimal tongThueAll = 0;
            if (query.Any()) tongThueAll = query.Sum(d => d.TienThue);
            var queryMonth = query.Where(d => d.NgayDat.Value.Month == selectedMonth && d.NgayDat.Value.Year == selectedYear);
            decimal thueThang = 0; decimal doanhThuTong = 0;
            if (queryMonth.Any())
            {
                thueThang = queryMonth.Sum(d => d.TienThue);
                doanhThuTong = queryMonth.Sum(d => d.TongTien);
            }
            decimal doanhThuNet = doanhThuTong - thueThang;
            var listDonHangThue = queryMonth.OrderByDescending(d => d.NgayDat).ToList();
            ViewBag.TongThueAll = tongThueAll; ViewBag.ThueThang = thueThang; ViewBag.DoanhThuNet = doanhThuNet;
            ViewBag.SelectedMonth = selectedMonth; ViewBag.SelectedYear = selectedYear;
            return View(listDonHangThue);
        }

        [Authorize(Roles = "Boss")]
        public IActionResult XuatExcelThue(int? month, int? year)
        {
            var now = DateTime.Now;
            int selectedYear = year ?? now.Year;
            int selectedMonth = month ?? now.Month;
            var query = _context.DonHangs.AsNoTracking().Where(d => d.TrangThai == 3 && d.NgayDat.Value.Month == selectedMonth && d.NgayDat.Value.Year == selectedYear).OrderByDescending(d => d.NgayDat).ToList();
            var sb = new StringBuilder();
            sb.AppendLine("Mã đơn hàng,Ngày hóa đơn,Tổng thanh toán,Thuế VAT (10%),Doanh thu Net");
            foreach (var item in query)
            {
                decimal net = item.TongTien - item.TienThue;
                string ngay = item.NgayDat.Value.ToString("dd/MM/yyyy HH:mm");
                sb.AppendLine($"{item.MaDon},{ngay},{item.TongTien},{item.TienThue},{net}");
            }
            string fileName = $"BaoCaoThue_T{selectedMonth}_{selectedYear}.csv";
            var fileContent = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(fileContent, "text/csv", fileName);
        }

        // --- 16. TÍNH LƯƠNG NHÂN VIÊN ---
        [Authorize(Roles = "Boss")]
        public IActionResult QuanLyLuong(int? month, int? year)
        {
            var now = DateTime.Now;
            int sMonth = month ?? now.Month;
            int sYear = year ?? now.Year;

            var admins = _context.TaiKhoans.Where(u => u.Role == "Admin").ToList();
            var configs = _context.CauHinhLuongs.ToList();
            var paidSalaries = _context.BangLuongs
                .Where(b => b.Thang == sMonth && b.Nam == sYear)
                .ToList();

            var payrollList = new List<PayrollViewModel>();

            foreach (var ad in admins)
            {
                var paid = paidSalaries.FirstOrDefault(p => p.TaiKhoanId == ad.Id);

                if (paid != null)
                {
                    double phanTram = 0;
                    if (paid.DoanhSoDatDuoc > 0)
                    {
                        phanTram = (double)(paid.TienHoaHong / paid.DoanhSoDatDuoc * 100);
                        phanTram = Math.Round(phanTram, 2);
                    }

                    payrollList.Add(new PayrollViewModel
                    {
                        TaiKhoanId = ad.Id,
                        FullName = ad.FullName,
                        Username = ad.Username,
                        StoreId = ad.StoreId,
                        LuongCung = paid.LuongCung,
                        PhanTramHoaHong = phanTram,
                        DoanhSo = paid.DoanhSoDatDuoc,
                        TienHoaHong = paid.TienHoaHong,
                        TongThucNhan = paid.TongThucNhan,
                        DaChot = true,
                        NgayChot = paid.NgayChot
                    });
                }
                else
                {
                    var config = configs.FirstOrDefault(c => c.TaiKhoanId == ad.Id);
                    decimal luongCung = config?.LuongCung ?? 0;
                    double phanTram = config?.PhanTramHoaHong ?? 0;

                    decimal doanhSo = 0;
                    if (ad.StoreId.HasValue)
                    {
                        doanhSo = _context.DonHangs
                            .Where(d => d.StoreId == ad.StoreId
                                     && d.TrangThai == 3
                                     && d.NgayDat.Value.Month == sMonth
                                     && d.NgayDat.Value.Year == sYear)
                            .Sum(d => d.TongTien - d.TienThue);
                    }

                    decimal hoaHong = doanhSo * (decimal)(phanTram / 100);

                    payrollList.Add(new PayrollViewModel
                    {
                        TaiKhoanId = ad.Id,
                        FullName = ad.FullName,
                        Username = ad.Username,
                        StoreId = ad.StoreId,
                        LuongCung = luongCung,
                        PhanTramHoaHong = phanTram,
                        DoanhSo = doanhSo,
                        TienHoaHong = hoaHong,
                        TongThucNhan = luongCung + hoaHong,
                        DaChot = false
                    });
                }
            }

            ViewBag.SelectedMonth = sMonth;
            ViewBag.SelectedYear = sYear;
            return View(payrollList);
        }

        [Authorize(Roles = "Boss")]
        [HttpPost]
        public IActionResult CapNhatCauHinhLuong(int taiKhoanId, decimal luongCung, double phanTram)
        {
            var config = _context.CauHinhLuongs.FirstOrDefault(c => c.TaiKhoanId == taiKhoanId);
            if (config == null)
            {
                config = new CauHinhLuong { TaiKhoanId = taiKhoanId };
                _context.CauHinhLuongs.Add(config);
            }
            config.LuongCung = luongCung;
            config.PhanTramHoaHong = phanTram;
            _context.SaveChanges();

            GhiNhatKy("Cấu hình lương", $"Cập nhật lương ID {taiKhoanId}: Cứng {luongCung:N0}, HH {phanTram}%");
            return Json(new { success = true });
        }

        [Authorize(Roles = "Boss")]
        [HttpPost]
        public IActionResult ChotLuong(int taiKhoanId, int month, int year)
        {
            if (_context.BangLuongs.Any(b => b.TaiKhoanId == taiKhoanId && b.Thang == month && b.Nam == year))
                return Json(new { success = false, message = "Đã chốt lương nhân viên này rồi!" });

            var user = _context.TaiKhoans.Find(taiKhoanId);
            var config = _context.CauHinhLuongs.FirstOrDefault(c => c.TaiKhoanId == taiKhoanId);

            decimal luongCung = config?.LuongCung ?? 0;
            double phanTram = config?.PhanTramHoaHong ?? 0;

            decimal doanhSo = 0;
            if (user.StoreId.HasValue)
            {
                doanhSo = _context.DonHangs
                    .Where(d => d.StoreId == user.StoreId && d.TrangThai == 3 && d.NgayDat.Value.Month == month && d.NgayDat.Value.Year == year)
                    .Sum(d => d.TongTien - d.TienThue);
            }
            decimal hoaHong = doanhSo * (decimal)(phanTram / 100);

            var bangLuong = new BangLuong
            {
                TaiKhoanId = taiKhoanId,
                Thang = month,
                Nam = year,
                DoanhSoDatDuoc = doanhSo,
                LuongCung = luongCung,
                TienHoaHong = hoaHong,
                TongThucNhan = luongCung + hoaHong,
                NgayChot = DateTime.Now,
                NguoiChot = User.Identity.Name
            };

            _context.BangLuongs.Add(bangLuong);
            _context.SaveChanges();

            GhiNhatKy("Chốt lương", $"Đã chốt lương tháng {month}/{year} cho {user.Username}. Tổng: {bangLuong.TongThucNhan:N0}");
            return Json(new { success = true });
        }

        [Authorize(Roles = "Boss")]
        [HttpPost]
        public IActionResult HuyChotLuong(int taiKhoanId, int month, int year)
        {
            var bangLuong = _context.BangLuongs.FirstOrDefault(b => b.TaiKhoanId == taiKhoanId && b.Thang == month && b.Nam == year);
            if (bangLuong != null)
            {
                var user = _context.TaiKhoans.Find(taiKhoanId);
                _context.BangLuongs.Remove(bangLuong);
                _context.SaveChanges();
                GhiNhatKy("Hủy chốt lương", $"Đã hủy chốt lương tháng {month}/{year} cho {user?.Username ?? taiKhoanId.ToString()}");
                return Json(new { success = true });
            }
            return Json(new { success = false, message = "Không tìm thấy bảng lương đã chốt" });
        }

        // --- XUẤT EXCEL BẢNG LƯƠNG ---
        [Authorize(Roles = "Boss")]
        [HttpGet]
        public IActionResult XuatExcelLuong(int? month, int? year)
        {
            var now = DateTime.Now;
            int sMonth = month ?? now.Month;
            int sYear = year ?? now.Year;

            var admins = _context.TaiKhoans.Where(u => u.Role == "Admin").ToList();
            var configs = _context.CauHinhLuongs.ToList();
            var paidSalaries = _context.BangLuongs.Where(b => b.Thang == sMonth && b.Nam == sYear).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("Nhân viên,Username,Chi nhánh,Lương cứng,% Hoa hồng,Doanh số,Tiền hoa hồng,Tổng thực nhận,Trạng thái,Ngày chốt");

            foreach (var ad in admins)
            {
                var paid = paidSalaries.FirstOrDefault(p => p.TaiKhoanId == ad.Id);
                string storeName = ad.StoreId switch { 1 => "Hà Nội", 2 => "Đà Nẵng", 3 => "TP.HCM", _ => "---" };

                if (paid != null)
                {
                    double phanTram = paid.DoanhSoDatDuoc > 0 ? Math.Round((double)(paid.TienHoaHong / paid.DoanhSoDatDuoc * 100), 2) : 0;
                    string ngayChot = paid.NgayChot.ToString("dd/MM/yyyy HH:mm");
                    sb.AppendLine($"\"{ad.FullName}\",{ad.Username},{storeName},{paid.LuongCung:N0},{phanTram}%,{paid.DoanhSoDatDuoc:N0},{paid.TienHoaHong:N0},{paid.TongThucNhan:N0},Đã chốt,{ngayChot}");
                }
                else
                {
                    var config = configs.FirstOrDefault(c => c.TaiKhoanId == ad.Id);
                    decimal luongCung = config?.LuongCung ?? 0;
                    double phanTram = config?.PhanTramHoaHong ?? 0;
                    decimal doanhSo = 0;
                    if (ad.StoreId.HasValue)
                    {
                        doanhSo = _context.DonHangs
                            .Where(d => d.StoreId == ad.StoreId && d.TrangThai == 3 && d.NgayDat.Value.Month == sMonth && d.NgayDat.Value.Year == sYear)
                            .Sum(d => d.TongTien - d.TienThue);
                    }
                    decimal hoaHong = doanhSo * (decimal)(phanTram / 100);
                    decimal tongNhan = luongCung + hoaHong;
                    sb.AppendLine($"\"{ad.FullName}\",{ad.Username},{storeName},{luongCung:N0},{phanTram}%,{doanhSo:N0},{hoaHong:N0},{tongNhan:N0},Chưa chốt,");
                }
            }

            string fileName = $"BangLuong_T{sMonth}_{sYear}.csv";
            var fileContent = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(fileContent, "text/csv", fileName);
        }

        // --- XÓA NHẬT KÝ HOẠT ĐỘNG ---
        [Authorize(Roles = "Boss")]
        [HttpPost]
        public IActionResult XoaNhatKy(int id)
        {
            try
            {
                var log = _context.LichSuHoatDongs.Find(id);
                if (log != null)
                {
                    _context.LichSuHoatDongs.Remove(log);
                    _context.SaveChanges();
                    TempData["Success"] = "Đã xóa bản ghi nhật ký.";
                }
                else
                {
                    TempData["Error"] = "Không tìm thấy bản ghi nhật ký!";
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Lỗi khi xóa: " + ex.Message;
            }
            return RedirectToAction("NhatKyHoatDong");
        }

        [Authorize(Roles = "Boss")]
        [HttpPost]
        public IActionResult XoaTatCaNhatKy()
        {
            try
            {
                var allLogs = _context.LichSuHoatDongs.ToList();
                if (allLogs.Any())
                {
                    _context.LichSuHoatDongs.RemoveRange(allLogs);
                    _context.SaveChanges();
                    TempData["Success"] = $"Đã dọn dẹp toàn bộ {allLogs.Count} bản ghi nhật ký!";
                }
                else
                {
                    TempData["Success"] = "Nhật ký hoạt động đã trống.";
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Lỗi khi dọn dẹp: " + ex.Message;
            }
            return RedirectToAction("NhatKyHoatDong");
        }

        // --- 17. THU NHẬP CỦA TÔI ---
        public IActionResult LuongCuaToi()
        {
            var user = _context.TaiKhoans.FirstOrDefault(u => u.Username == User.Identity.Name);
            if (user == null) return RedirectToAction("DangNhap", "Login");

            var config = _context.CauHinhLuongs.FirstOrDefault(c => c.TaiKhoanId == user.Id);
            decimal luongCung = config?.LuongCung ?? 0;
            double phanTram = config?.PhanTramHoaHong ?? 0;

            var now = DateTime.Now;
            decimal doanhSoThangNay = 0;

            if (user.StoreId.HasValue)
            {
                doanhSoThangNay = _context.DonHangs
                    .Where(d => d.StoreId == user.StoreId
                             && d.TrangThai == 3
                             && d.NgayDat.Value.Month == now.Month
                             && d.NgayDat.Value.Year == now.Year)
                    .Sum(d => d.TongTien - d.TienThue);
            }

            decimal hoaHongDuKien = doanhSoThangNay * (decimal)(phanTram / 100);

            var lichSuLuong = _context.BangLuongs
                .Where(b => b.TaiKhoanId == user.Id)
                .OrderByDescending(b => b.Nam).ThenByDescending(b => b.Thang)
                .ToList();

            ViewBag.ThangNay = now.Month;
            ViewBag.LuongCung = luongCung;
            ViewBag.PhanTram = phanTram;
            ViewBag.DoanhSo = doanhSoThangNay;
            ViewBag.HoaHong = hoaHongDuKien;
            ViewBag.TongDuKien = luongCung + hoaHongDuKien;

            return View(lichSuLuong);
        }

        // --- ĐIỀU PHỐI KHO (FORWARDING ENDPOINTS) ---
        [HttpPost]
        public async Task<IActionResult> DuyetYeuCau(int id, int tuKhoId)
        {
            if (!User.IsInRole("Boss")) return Json(new { success = false, message = "Quyền hạn không đủ" });
            var phieu = await _context.PhieuChuyenKhos.FindAsync(id);
            if (phieu == null) return Json(new { success = false, message = "Không tìm thấy phiếu" });
            if (tuKhoId == phieu.DenKhoId) return Json(new { success = false, message = "Kho đi/đến trùng nhau!" });
            phieu.TuKhoId = tuKhoId;
            await _context.SaveChangesAsync();
            TaoThongBaoHeThong("Lệnh xuất hàng", $"Boss duyệt yêu cầu #{phieu.MaPhieu}. Xuất hàng ngay.", phieu.Id.ToString(), "QuanLyChuyenKho", tuKhoId);
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> DuyetXuatKho(int id)
        {
            var phieu = await _context.PhieuChuyenKhos.Include(p => p.SanPham).FirstOrDefaultAsync(p => p.Id == id);
            if (phieu == null || phieu.TrangThai != 0) return Json(new { success = false, message = "Lỗi trạng thái" });
            var khoDi = await _context.KhoHangs.FirstOrDefaultAsync(k => k.SanPhamId == phieu.SanPhamId && k.StoreId == phieu.TuKhoId);
            if (khoDi == null || khoDi.SoLuong < phieu.SoLuong)
                return Json(new { success = false, message = $"Kho đi ({GetStoreName(phieu.TuKhoId)}) không đủ hàng!" });
            khoDi.SoLuong -= phieu.SoLuong;
            phieu.TrangThai = 1;
            await _context.SaveChangesAsync();
            TaoThongBaoHeThong("Hàng đang đến", $"Kho {GetStoreName(phieu.TuKhoId)} đã xuất phiếu {phieu.MaPhieu}.", phieu.Id.ToString(), "QuanLyChuyenKho", phieu.DenKhoId);
            GhiNhatKy("Xuất kho", $"Xuất {phieu.SoLuong} {phieu.SanPham.Name} đi {GetStoreName(phieu.DenKhoId)}");
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> XacNhanNhanHang(int id)
        {
            var phieu = await _context.PhieuChuyenKhos.Include(p => p.SanPham).FirstOrDefaultAsync(p => p.Id == id);
            if (phieu == null || phieu.TrangThai != 1) return Json(new { success = false });
            var khoDen = await _context.KhoHangs.FirstOrDefaultAsync(k => k.SanPhamId == phieu.SanPhamId && k.StoreId == phieu.DenKhoId);
            if (khoDen != null) khoDen.SoLuong += phieu.SoLuong;
            else _context.KhoHangs.Add(new KhoHang { SanPhamId = phieu.SanPhamId, StoreId = phieu.DenKhoId, SoLuong = phieu.SoLuong });
            phieu.TrangThai = 2;
            phieu.NgayHoanThanh = DateTime.Now;
            await _context.SaveChangesAsync();
            TaoThongBaoHeThong("Hoàn tất", $"Phiếu {phieu.MaPhieu} đã hoàn thành.", phieu.Id.ToString(), "QuanLyChuyenKho", null);
            GhiNhatKy("Nhập kho", $"Nhận {phieu.SoLuong} {phieu.SanPham.Name} từ {GetStoreName(phieu.TuKhoId)}");
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

        // --- 13. TÍNH NĂNG KANBAN ĐIỀU PHỐI CÔNG VIỆC & PHÂN VIỆC PHÒNG BAN ---
        private void EnsureKanbanTableCreated()
        {
            try
            {
                string sql = @"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='CongViecKanbans' AND xtype='U')
BEGIN
    CREATE TABLE [dbo].[CongViecKanbans] (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [TieuDe] NVARCHAR(250) NOT NULL,
        [MoTa] NVARCHAR(MAX) NULL,
        [PhongBan] NVARCHAR(100) NOT NULL DEFAULT N'Kho & Cung ứng',
        [NguoiThucHienId] INT NULL,
        [TenNguoiThucHien] NVARCHAR(100) NULL,
        [NguoiGiaoViec] NVARCHAR(50) NULL,
        [TrangThai] INT NOT NULL DEFAULT 0,
        [DoUuTien] INT NOT NULL DEFAULT 1,
        [StoreId] INT NULL,
        [HanChot] DATETIME2 NULL,
        [NgayTao] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [NgayHoanThanh] DATETIME2 NULL
    );
END";
                _context.Database.ExecuteSqlRaw(sql);
            }
            catch { }
        }

        [Authorize(Roles = "Boss,Admin")]
        public IActionResult Kanban(string? phongBan, int? storeId, int? doUuTien, string? search)
        {
            EnsureKanbanTableCreated();

            var query = _context.CongViecKanbans.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(phongBan) && phongBan != "all")
            {
                query = query.Where(k => k.PhongBan == phongBan);
            }

            if (storeId.HasValue && storeId.Value != 0)
            {
                query = query.Where(k => k.StoreId == storeId.Value);
            }

            if (doUuTien.HasValue && doUuTien.Value >= 0)
            {
                query = query.Where(k => k.DoUuTien == doUuTien.Value);
            }

            if (!string.IsNullOrEmpty(search))
            {
                string s = search.ToLower().Trim();
                query = query.Where(k => k.TieuDe.ToLower().Contains(s) || (k.TenNguoiThucHien != null && k.TenNguoiThucHien.ToLower().Contains(s)));
            }

            var list = query.OrderByDescending(k => k.DoUuTien).ThenByDescending(k => k.NgayTao).ToList();

            ViewBag.NhanViens = _context.TaiKhoans.AsNoTracking()
                .Where(t => t.Role == "Admin" || t.Role == "Boss")
                .ToList();

            ViewBag.PhongBanHienTai = phongBan ?? "all";
            ViewBag.StoreIdHienTai = storeId ?? 0;
            ViewBag.DoUuTienHienTai = doUuTien ?? -1;
            ViewBag.SearchHienTai = search ?? "";

            return View(list);
        }

        [Authorize(Roles = "Boss,Admin")]
        [HttpPost]
        public IActionResult ThemCongViec(CongViecKanban model)
        {
            EnsureKanbanTableCreated();

            if (string.IsNullOrWhiteSpace(model.TieuDe))
            {
                TempData["Error"] = "Vui lòng nhập tiêu đề công việc!";
                return RedirectToAction("Kanban");
            }

            if (model.NguoiThucHienId.HasValue)
            {
                var tk = _context.TaiKhoans.Find(model.NguoiThucHienId.Value);
                if (tk != null)
                {
                    model.TenNguoiThucHien = tk.FullName;
                    if (!model.StoreId.HasValue && tk.StoreId.HasValue)
                    {
                        model.StoreId = tk.StoreId;
                    }
                }
            }

            model.NguoiGiaoViec = User.Identity?.Name ?? "Admin";
            model.NgayTao = DateTime.Now;

            _context.CongViecKanbans.Add(model);
            _context.SaveChanges();

            GhiNhatKy("Giao việc", $"Đã giao việc: '{model.TieuDe}' cho {model.TenNguoiThucHien ?? "Phòng ban"}");
            TempData["Success"] = "Đã tạo và phân công công việc mới thành công!";
            return RedirectToAction("Kanban");
        }

        [Authorize(Roles = "Boss,Admin")]
        [HttpPost]
        public IActionResult CapNhatTrangThaiKanban(int id, int trangThai)
        {
            EnsureKanbanTableCreated();

            var task = _context.CongViecKanbans.Find(id);
            if (task == null) return Json(new { success = false, message = "Không tìm thấy công việc" });

            task.TrangThai = trangThai;
            if (trangThai == 3) // Hoàn thành
            {
                task.NgayHoanThanh = DateTime.Now;
            }
            else
            {
                task.NgayHoanThanh = null;
            }

            _context.SaveChanges();
            GhiNhatKy("Cập nhật tiến độ", $"Chuyển trạng thái công việc #{task.Id} sang {trangThai}");

            return Json(new { success = true });
        }

        [Authorize(Roles = "Boss,Admin")]
        [HttpPost]
        public IActionResult SuaCongViecKanban(CongViecKanban model)
        {
            EnsureKanbanTableCreated();

            var task = _context.CongViecKanbans.Find(model.Id);
            if (task == null)
            {
                TempData["Error"] = "Không tìm thấy công việc cần sửa!";
                return RedirectToAction("Kanban");
            }

            task.TieuDe = model.TieuDe;
            task.MoTa = model.MoTa;
            task.PhongBan = model.PhongBan;
            task.DoUuTien = model.DoUuTien;
            task.TrangThai = model.TrangThai;
            task.StoreId = model.StoreId;
            task.HanChot = model.HanChot;

            if (model.NguoiThucHienId.HasValue)
            {
                task.NguoiThucHienId = model.NguoiThucHienId;
                var tk = _context.TaiKhoans.Find(model.NguoiThucHienId.Value);
                if (tk != null)
                {
                    task.TenNguoiThucHien = tk.FullName;
                }
            }

            if (task.TrangThai == 3 && task.NgayHoanThanh == null)
            {
                task.NgayHoanThanh = DateTime.Now;
            }

            _context.SaveChanges();
            TempData["Success"] = $"Đã cập nhật công việc '{task.TieuDe}' thành công!";
            return RedirectToAction("Kanban");
        }

        [Authorize(Roles = "Boss,Admin")]
        [HttpPost]
        public IActionResult XoaCongViecKanban(int id)
        {
            EnsureKanbanTableCreated();

            var task = _context.CongViecKanbans.Find(id);
            if (task != null)
            {
                string title = task.TieuDe;
                _context.CongViecKanbans.Remove(task);
                _context.SaveChanges();
                GhiNhatKy("Xóa công việc", $"Đã xóa công việc '{title}'");
                TempData["Success"] = "Đã xóa công việc thành công!";
            }
            return RedirectToAction("Kanban");
        }

        [Authorize(Roles = "Boss,Admin")]
        [HttpPost]
        public IActionResult XoaTatCaCongViecKanban()
        {
            EnsureKanbanTableCreated();
            var all = _context.CongViecKanbans.ToList();
            if (all.Any())
            {
                _context.CongViecKanbans.RemoveRange(all);
                _context.SaveChanges();
                GhiNhatKy("Dọn dẹp bảng điều phối", "Đã xóa toàn bộ công việc trên bảng điều phối");
                TempData["Success"] = "Đã dọn sạch toàn bộ công việc trên bảng điều phối!";
            }
            return RedirectToAction("Kanban");
        }
    }
}