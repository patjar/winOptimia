# EPF Optimizer Pro Premium v3.6

Version avec moteur de tâches indépendantes, progression individuelle et parallélisme adaptatif selon CPU/RAM.

## Correctif v3.6

- Corrige les chaînes C# mal échappées dans `MainWindow.xaml.cs`.
- Corrige les chemins Windows et le rapport HTML dans `AdaptiveTaskEngine.cs`.
- Conserve le moteur de tâches indépendantes et les workers adaptatifs.

## Nouveautés v3.2

- Chaque action est une tâche indépendante.
- Chaque tâche possède sa propre carte, son statut, son message et sa progression.
- Le nombre de tâches simultanées est calculé au lancement selon CPU/RAM.
- Modes adaptatifs : Performance, Équilibré, Protection, Sécurité maximale.
- Windows Update est vérifié avec Microsoft.Update.Session, sans installation automatique.
- Rapport HTML enrichi avec les tâches, l'IA locale et le journal.

## Compilation

```powershell
dotnet clean
dotnet restore
dotnet build .\EPFOptimizerPro.csproj -c Release
```

## Exécution

```powershell
.\bin\Release\net10.0-windows\EPFOptimizerPro.exe
```

Pour toutes les actions système, lance l'application en administrateur.


## Dashboard monitoring v3.6

- Cartes de tâches compactes pour faire rentrer toutes les tâches dans la page.
- Noms raccourcis : Audit, Updates, Temp User, Temp Win, Corbeille, DNS, Volumes, SFC.
- Hauteur réduite des cartes et messages condensés.
- Suppression de la barre de défilement verticale dans la zone des tâches.
- Résumé visible : tâches terminées, en cours, en avertissement et en erreur.


## Actives et terminées v3.6

- Les tâches en cours restent dans la zone principale "Tâches actives".
- Les tâches terminées quittent la zone active et se rangent automatiquement en mini-badges.
- Les mini-badges affichent une icône et un nom court : Audit, Updates, Temp User, Temp Win, Corbeille, DNS, Volumes, SFC.
- Le résumé indique explicitement quelles tâches sont encore en cours avec leur pourcentage.


## Optimisations anti-ralentissement v3.6

- Les mises à jour de progression globale sont limitées par un throttle de 300 ms.
- Les tâches ne republient leur progression que par palier significatif pour éviter de saturer l'interface.
- Les mises à jour de cartes passent par `Dispatcher.BeginInvoke` avec priorité basse au lieu de bloquer les workers.
- Le résumé du dashboard est recalculé par le timer principal plutôt qu'à chaque log.
- Le journal UI garde une taille maximale pour éviter les ralentissements lorsque beaucoup de lignes sont affichées.
- L'historique interne des logs est limité aux 500 dernières entrées.
