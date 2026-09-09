-- 1. Remover o índice único anterior da ConferenciaMensal
DROP INDEX [IX_ConferenciasMensais_CentroCustoId_Ano_Mes] ON [ConferenciasMensais];

-- Recriar o índice, mas sem ser UNIQUE
CREATE INDEX [IX_ConferenciasMensais_CentroCustoId_Ano_Mes] ON [ConferenciasMensais] ([CentroCustoId], [Ano], [Mes]);

-- 2. Adicionar as métricas na tabela ConferenciaMensal
ALTER TABLE [ConferenciasMensais] ADD [QtdViagensOk] int NOT NULL DEFAULT 0;
ALTER TABLE [ConferenciasMensais] ADD [QtdViagensContestadas] int NOT NULL DEFAULT 0;
ALTER TABLE [ConferenciasMensais] ADD [QtdViagensOutroGestor] int NOT NULL DEFAULT 0;

-- 3. Adicionar o rastreio de quem conferiu na tabela Viagem
ALTER TABLE [Viagens] ADD [ConferidoPorUsuarioId] int NULL;

-- 4. Adicionar a Foreign Key
ALTER TABLE [Viagens] ADD CONSTRAINT [FK_Viagens_Usuarios_ConferidoPorUsuarioId] FOREIGN KEY ([ConferidoPorUsuarioId]) REFERENCES [Usuarios] ([Id]) ON DELETE NO ACTION;

-- 5. Atualizar a tabela de migrações para refletir essa mudança no Entity Framework
INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES ('20240720_AddAtestesHistorico', '8.0.0');
