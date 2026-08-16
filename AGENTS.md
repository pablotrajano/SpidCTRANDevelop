# Instruções do projeto Spid

## Procedimento permanente de PULL

Quando o usuário solicitar **"dar um PULL"** nesta solução, execute a partir da
raiz `P:\Solução Spid\Spid`, nesta ordem exata:

```powershell
git fetch origin
git pull --ff-only origin main
git status -sb
```

Se o `--ff-only` falhar por divergência ou conflito, interrompa e informe o
usuário. Nunca use `git reset --hard`, `git checkout --` ou outra operação
destrutiva para forçar a sincronização sem autorização explícita.

## Procedimento permanente de PUSH

Quando o usuário solicitar **"dar um PUSH"** nesta solução, execute a partir da
raiz `P:\Solução Spid\Spid`, nesta ordem:

```powershell
git status -sb
git branch --show-current
git diff --check
git add <arquivos-da-alteração>
git commit -m "<tipo>: <descrição>"
git push origin main
git status -sb
```

Adicione somente os arquivos da alteração autorizada; não use `git add .`
automaticamente. Se a branch atual não for `main`, interrompa antes do push e
informe o usuário. Se o commit ou o push falhar, preserve o estado e informe o
erro; nunca use `git push --force` sem autorização explícita.
