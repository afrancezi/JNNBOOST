using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JnnBoost.Api.Models;

[Table("tentativas_bloqueadas")]
public class TentativaBloqueada
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("licenca_id")]
    public int LicencaId { get; set; }

    [ForeignKey(nameof(LicencaId))]
    public Licenca? Licenca { get; set; }

    [Column("hwid_tentativa")]
    [MaxLength(255)]
    public string HwidTentativa { get; set; } = string.Empty;

    [Column("tentativa_em")]
    public DateTime TentativaEm { get; set; } = DateTime.UtcNow;
}
