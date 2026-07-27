using Microsoft.EntityFrameworkCore;

namespace Spid.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<CentroCusto> CentrosCusto => Set<CentroCusto>();
    public DbSet<Colaborador> Colaboradores => Set<Colaborador>();
    public DbSet<ParceiroViagem> Parceiros => Set<ParceiroViagem>();
    public DbSet<Viagem> Viagens => Set<Viagem>();
    public DbSet<Recurso> Recursos => Set<Recurso>();
    public DbSet<PerfilRecurso> PerfisRecurso => Set<PerfilRecurso>();
    public DbSet<ConferenciaMensal> ConferenciasMensais => Set<ConferenciaMensal>();
    public DbSet<EncerramentoMensal> EncerramentosMensais => Set<EncerramentoMensal>();
    public DbSet<ImportacaoLog> ImportacoesLog => Set<ImportacaoLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Índice único para o Ponto: usado como login
        modelBuilder.Entity<Usuario>()
            .HasIndex(u => u.Ponto)
            .IsUnique();

        modelBuilder.Entity<ImportacaoLog>()
            .HasOne(l => l.Usuario)
            .WithMany()
            .HasForeignKey(l => l.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);

        // Relacionamento: 1 CentroCusto -> N Usuários (gestores)
        modelBuilder.Entity<Usuario>()
            .HasOne(u => u.CentroCusto)
            .WithMany(s => s.Usuarios)
            .HasForeignKey(u => u.CentroCustoId)
            .OnDelete(DeleteBehavior.SetNull);

        // Índice único no IdViagemParceiro para deduplicação na importação
        modelBuilder.Entity<Viagem>()
            .HasIndex(v => v.IdViagemParceiro)
            .IsUnique();

        // SQL Server não permite múltiplos caminhos de cascade delete.
        // Caminho 1: CentrosCusto → Colaboradores (CASCADE) → Viagens (CASCADE)
        // Caminho 2: CentrosCusto → Viagens (direto - deve ser RESTRICT)
        modelBuilder.Entity<Viagem>()
            .HasOne(v => v.CentroCusto)
            .WithMany(s => s.Viagens)
            .HasForeignKey(v => v.CentroCustoId)
            .OnDelete(DeleteBehavior.Restrict);

        // Idem: ConferenciaMensal referencia CentroCusto e Usuario (que também referencia CentroCusto)
        modelBuilder.Entity<ConferenciaMensal>()
            .HasOne(c => c.CentroCusto)
            .WithMany()
            .HasForeignKey(c => c.CentroCustoId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ConferenciaMensal>()
            .HasOne(c => c.Usuario)
            .WithMany()
            .HasForeignKey(c => c.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);

        // Precisão explícita para campos monetários (evita truncamento no SQL Server)
        modelBuilder.Entity<Viagem>()
            .Property(v => v.ValorCotado)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Viagem>()
            .Property(v => v.ValorFinal)
            .HasPrecision(18, 2);

        // Precisão time(0) para horários — armazena apenas HH:MM:SS, sem frações
        modelBuilder.Entity<Viagem>()
            .Property(v => v.HoraSolicitacao)
            .HasColumnType("time(0)");

        modelBuilder.Entity<Viagem>()
            .Property(v => v.HoraInicio)
            .HasColumnType("time(0)");

        modelBuilder.Entity<Viagem>()
            .Property(v => v.HoraFim)
            .HasColumnType("time(0)");

        // Índice único na Chave do recurso
        modelBuilder.Entity<Recurso>()
            .HasIndex(r => r.Chave)
            .IsUnique();

        // Índice composto único: um perfil só pode ter cada recurso uma vez
        modelBuilder.Entity<PerfilRecurso>()
            .HasIndex(pr => new { pr.Perfil, pr.RecursoId })
            .IsUnique();

        // Índice: confirmações por centroCusto/mês/ano (pode haver mais de uma agora)
        modelBuilder.Entity<ConferenciaMensal>()
            .HasIndex(c => new { c.CentroCustoId, c.Ano, c.Mes });

        modelBuilder.Entity<Viagem>()
            .HasOne(v => v.ConferidoPorUsuario)
            .WithMany()
            .HasForeignKey(v => v.ConferidoPorUsuarioId)
            .OnDelete(DeleteBehavior.Restrict);

        // EncerramentoMensal: índice único por Ano+Mês (global)
        modelBuilder.Entity<EncerramentoMensal>()
            .HasIndex(e => new { e.Ano, e.Mes })
            .IsUnique();

        modelBuilder.Entity<EncerramentoMensal>()
            .HasOne(e => e.EncerradoPor)
            .WithMany()
            .HasForeignKey(e => e.EncerradoPorId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<EncerramentoMensal>()
            .HasOne(e => e.LiberadoPor)
            .WithMany()
            .HasForeignKey(e => e.LiberadoPorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}