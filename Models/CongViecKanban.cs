using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace banthietbidientu.Models
{
    [Table("CongViecKanbans")]
    public class CongViecKanban
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "Tiêu đề công việc không được để trống")]
        [StringLength(250)]
        public string TieuDe { get; set; } = string.Empty;

        public string? MoTa { get; set; }

        // Phòng ban: "Kho & Cung ứng", "Bán hàng & Thu ngân", "Giao vận & Điều phối", "CSKH & Kỹ thuật", "Kế toán & Tài chính", "Ban Giám Đốc"
        [StringLength(100)]
        public string PhongBan { get; set; } = "Kho & Cung ứng";

        // Người thực hiện (FK tới TaiKhoan)
        public int? NguoiThucHienId { get; set; }

        [ForeignKey("NguoiThucHienId")]
        public virtual TaiKhoan? NguoiThucHien { get; set; }

        [StringLength(100)]
        public string? TenNguoiThucHien { get; set; }

        // Người giao việc (Username)
        [StringLength(50)]
        public string? NguoiGiaoViec { get; set; }

        // Trạng thái Kanban:
        // 0: Cần làm (To Do)
        // 1: Đang thực hiện (In Progress)
        // 2: Chờ kiểm tra (Review)
        // 3: Hoàn thành (Done)
        public int TrangThai { get; set; } = 0;

        // Mức độ ưu tiên:
        // 0: Thấp (Low)
        // 1: Bình thường (Normal)
        // 2: Cao (High)
        // 3: Khẩn cấp (Urgent)
        public int DoUuTien { get; set; } = 1;

        // Chi nhánh áp dụng (null = Toàn hệ thống, 1: Hà Nội, 2: Đà Nẵng, 3: TP.HCM)
        public int? StoreId { get; set; }

        public DateTime? HanChot { get; set; }

        public DateTime NgayTao { get; set; } = DateTime.Now;

        public DateTime? NgayHoanThanh { get; set; }
    }
}
