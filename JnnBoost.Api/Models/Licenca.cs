using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JnnBoost.Api.Models;

[Table("licencas")]
public class Licenca
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("chave_licenca")]
    [MaxLength(255)]
    public string ChaveLicenca { get; set; } = string.Empty;

    [Column("hwid")]
    [MaxLength(255)]
    public string? Hwid { get; set; }

    [Column("data_ativacao")]
    public DateTime? DataAtivacao { get; set; }

    [Column("ativa")]
    public bool Ativa { get; set; } = false;

    [Column("criada_em")]
    public DateTime CriadaEm { get; set; } = DateTime.UtcNow;

    // Data em que o acesso deixa de ser válido. Null = sem expiração (acesso vitalício).
    [Column("data_expiracao")]
    public DateTime? DataExpiracao { get; set; }

    // Revogação manual (ex: reembolso, banimento por compartilhamento).
    // Diferente de expiração: uma vez revogada, a licença NUNCA reativa
    // sozinha, mesmo que alguém chame /api/validar de novo.
    [Column("revogada")]
    public bool Revogada { get; set; } = false;

    // Navegação: uma licença pode ter várias tentativas bloqueadas
    public ICollection<TentativaBloqueada> TentativasBloqueadas { get; set; } = new List<TentativaBloqueada>();
}
