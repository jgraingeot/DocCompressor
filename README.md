# Compresseur de documents

Application de bureau (Windows et Linux) qui réduit des photos, des scans et des PDF pour qu'**aucun fichier ne dépasse une taille maximale : 1 Mo par défaut, réglable** (en Ko ou en Mo). Typiquement pour les pièces justificatives demandées par les organismes : carte d'identité, justificatif de domicile, avis d'imposition…

## Utilisation

1. Glissez-déposez les fichiers (ou un dossier) dans la fenêtre, ou cliquez sur « Ajouter des fichiers… ». Les fichiers peuvent aussi être passés en arguments (`DocCompressor carte.jpg avis.pdf`), ce qui permet de les déposer sur l'icône du programme.
2. Réglez la taille maximale par fichier.
3. Choisissez où enregistrer les résultats :
   - **à côté de chaque original**, avec le suffixe `_compresse` (`carte.jpg` → `carte_compresse.jpg`) ;
   - **dans un dossier**, sous les noms d'origine (pratique pour tout envoyer d'un coup).
4. Cliquez sur « Compresser ». Le bouton « Voir » ouvre un résultat pour vérifier qu'il reste lisible.

Les originaux ne sont jamais modifiés ni écrasés. Les réglages sont mémorisés (`%APPDATA%\DocCompressor` sous Windows, `~/.config/DocCompressor` sous Linux).

### Regrouper plusieurs fichiers dans un seul PDF (recto/verso)

Beaucoup d'organismes demandent le recto et le verso d'une carte d'identité dans **un seul fichier**. Pour cela :

1. Ajoutez les fichiers (photos, scans, PDF) et cochez « Regrouper tous les fichiers dans un seul PDF ».
2. Mettez-les dans l'ordre voulu avec les boutons ▲ et ▼ : c'est l'ordre des pages du PDF.
3. Cochez « Deux images par page » pour placer le recto et le verso l'un au-dessus de l'autre sur la même page A4 (comme une photocopie). Sinon, chaque image occupe sa propre page.
4. Choisissez le nom du PDF : par défaut, la partie commune des noms (`carte_identite_recto.jpg` + `carte_identite_verso.jpg` → `carte_identite.pdf`).
5. Cliquez sur « Fusionner ». Le PDF obtenu respecte la taille maximale.

Les pages des PDF ajoutés sont reprises telles quelles (texte conservé), puis l'ensemble est compressé comme un PDF ordinaire. Le PDF fusionné est enregistré dans le dossier du premier fichier, ou dans le dossier choisi.

## Fonctionnement

| Fichier | Traitement |
|---------|------------|
| Image JPG/PNG déjà sous la limite | Copiée telle quelle |
| Image au-dessus de la limite (ou WebP, BMP, GIF) | Réencodée en JPEG avec la meilleure qualité qui tient dans la limite. Si nécessaire, la définition est réduite par paliers plutôt que de trop dégrader la qualité |
| PDF déjà sous la limite | Copié tel quel |
| PDF au-dessus de la limite | 1. Les images du PDF sont recompressées : **le texte, les polices et les dessins sont conservés**<br>2. Si cela ne suffit pas (ou si le PDF est protégé contre la modification), les pages sont converties en images, de 200 à 50 ppp : le texte n'est alors plus sélectionnable |

Au passage :
- l'orientation des photos de téléphone (EXIF) est appliquée, la transparence est remplacée par du blanc ;
- les scans en noir et blanc sont enregistrés en niveaux de gris (bien plus légers à qualité égale) ;
- les métadonnées des images réencodées (position GPS…) sont supprimées.

L'option « Convertir les photos et scans en PDF » place chaque image sur une page A4, pour les organismes qui n'acceptent que le PDF.

**1 Mo = 1 000 000 octets** : la limite est donc aussi respectée par un organisme qui compte 1 Mo = 1 048 576 octets.

## Formats et limites

- Formats acceptés : PDF, JPG/JPEG/JFIF, PNG, WebP, BMP, GIF.
- Non pris en charge : photos HEIC d'iPhone (les exporter en JPEG, ou régler l'appareil photo sur « Le plus compatible »), TIFF.
- Un PDF protégé par un mot de passe d'ouverture est refusé : il faut d'abord retirer la protection.
- Une signature électronique contenue dans un PDF n'est plus valide après compression.
- Si un document ne peut pas passer sous la limite en restant lisible (beaucoup de pages et limite très basse), un message l'indique plutôt que de produire un fichier illisible.

## Compilation et publication

Prérequis : SDK .NET 8.

```bash
# Lancer l'application
dotnet run --project DocCompressor/DocCompressor.csproj

# Lancer les tests du moteur de compression
dotnet test

# Produire les exécutables autonomes Linux et Windows (dans publish/linux-x64 et publish/win-x64)
./publish.sh
# ou, depuis Windows :
.\publish.ps1
```

Chaque plateforme donne **un seul exécutable autonome** (environ 50 Mo) qui ne nécessite pas d'installer .NET. Sous Linux, il faut les bibliothèques habituelles d'une application graphique (X11, fontconfig).

Bibliothèques utilisées : Avalonia et SkiaSharp (MIT), PDFsharp 6.2.4 (MIT — les versions 6.5 et suivantes sont sous licence AGPL, ne pas mettre à jour sans vérifier), PDFium via les paquets `bblanchon.PDFium` (Apache 2.0).

## Structure

```
/
├── DocCompressor.sln
├── publish.sh / publish.ps1           # Publication des exécutables autonomes
│
├── DocCompressor/                     # Application Avalonia
│   ├── MainWindow.axaml               # Fenêtre : liste des fichiers, réglages, progression
│   ├── ViewModels/                    # Modèles de vue (fenêtre, fichier de la liste)
│   └── AppSettings.cs                 # Réglages mémorisés
│
├── DocCompressor.Core/                # Moteur de compression (sans interface)
│   ├── DocumentCompressor.cs          # Points d'entrée : compression d'un fichier, fusion en un PDF
│   ├── FileCompressor.cs              # Enregistrement des résultats, noms de fichiers
│   ├── Imaging/                       # Décodage, réencodage JPEG, recherche de la qualité
│   └── Pdf/                           # Fusion, recompression des images (PDFsharp), rendu des pages (PDFium)
│
└── DocCompressor.Tests/               # Tests (xUnit) avec documents générés
```
