# Licences des données / Data licences

La version française fait foi. The French version is the reference; the English follows it.

## Banque du Canada : API Valet

- **Source :** https://www.bankofcanada.ca/valet (API Valet de la Banque du Canada).
- **Conditions lues :** https://www.bankofcanada.ca/terms/ (« Conditions d'utilisation »), lues le 2026-11-23 ;
  guide https://www.bankofcanada.ca/valet-api-how-to/, lu le même jour.
- **Ce qui est permis (§1) :** utiliser, copier, distribuer et transmettre librement le contenu du site, y compris
  à des fins commerciales, aux conditions suivantes :
  - **§1.1 Attribution :** nommer la Banque du Canada comme source et indiquer toute modification, sans laisser
    entendre que la Banque appuie ce projet ou son usage.
  - **§1.2 Exactitude :** faire preuve de diligence quant à l'exactitude du contenu reproduit.
  - **§1.3 Avis :** si le contenu est fourni dans un service payant ou un document vendu, informer l'acheteur qu'il
    provient du site de la Banque, où il est gratuit. Ce projet n'est ni payant ni vendu.
- **Exceptions (§2) :** le logo et le mot-symbole de la Banque ne sont pas reproduits ici (§2.2). Le contenu de
  tiers n'est pas couvert par la permission (§2.3) ; la Banque indique que les données sous-jacentes de ses taux
  de change proviennent de LSEG (anciennement Refinitiv). Ce projet n'utilise que les taux publiés par la Banque,
  jamais les données sous-jacentes.
- **Limites de requêtes (§3) :** il est interdit de contourner les limites imposées par la Banque sur le nombre ou
  la fréquence des requêtes, Valet compris. Aucune limite chiffrée n'apparaît dans les pages lues ; cela ne
  prouve pas qu'il n'en existe aucune. Le guide recommande : démarrer lentement, attendre avant de réessayer
  (délai croissant), et mettre en cache les données qui ne changent qu'une fois par jour. Ce projet met chaque
  réponse en cache et ne refait jamais une requête en boucle.
- **Clé d'accès :** aucune. Le guide : « No registration is required to use the Valet API. You do not need an
  access key. » La spécification OpenAPI (`/valet/static/swagger/api-en.yml`) ne déclare aucun schéma de sécurité.
- **Nature des taux :** la Banque qualifie tous ses taux de change d'**indicatifs**, publiés à des fins
  statistiques et analytiques, et non comme référence pour exécuter des opérations de change. Ici, ils valorisent
  un portefeuille fictif ; ce n'est pas un prix d'exécution.
- **Attribution utilisée par ce projet :** « Taux de change : Banque du Canada, API Valet
  (https://www.bankofcanada.ca/valet). Taux indicatifs. Reproduits sans modification de valeur ; sélection de
  séries et de dates. La Banque du Canada n'appuie pas ce projet. » Cette mention apparaît dans le README, dans
  la sortie JSON du livre et à côté de tout fichier de réponse Valet versé au dépôt.

### Séries constatées à la première requête (2026-11-23)

- Groupe `FX_RATES_DAILY` : 27 séries déclarées (`FX<devise>CAD`, une unité de devise en dollars canadiens). Du
  2026-11-02 au 2026-11-23, 24 ont une valeur chaque jour ; `FXRUBCAD`, `FXSARCAD` et `FXVNDCAD` n'en ont aucune.
- Le 2026-11-11 (jour du Souvenir) n'a aucune observation : un jour ouvrable sans taux publié.
- Il existe aussi des rendements obligataires de référence (groupe `bond_yields_benchmark` : 2, 3, 5, 7, 10 ans,
  long terme, obligations à rendement réel) et le CORRA. Ils ne sont pas utilisés dans la première version.

## Aucune autre source

Aucune autre donnée n'est téléchargée. Les opérations, positions, contreparties et noms de pupitre sont inventés
et marqués comme fictifs.

---

## Bank of Canada: Valet API (English)

- **Source:** https://www.bankofcanada.ca/valet (the Bank of Canada's Valet API).
- **Terms read:** https://www.bankofcanada.ca/terms/ ("Terms of Use"), read 2026-11-23; how-to guide
  https://www.bankofcanada.ca/valet-api-how-to/, read the same day.
- **What is permitted (§1):** freely use, copy, distribute and transmit the website's content, commercial use
  included, on these terms:
  - **§1.1 Attribution:** name the Bank of Canada as the source and say if changes were made, in no way that
    suggests the Bank endorses this project or its use.
  - **§1.2 Accuracy:** exercise due diligence on the accuracy of what is reproduced.
  - **§1.3 Notice:** content provided through a paid service or in a document for sale requires telling the buyer
    it came from the Bank's site, where it is free. This project is neither paid nor sold.
- **Exceptions (§2):** the Bank's logo and wordmark are not reproduced here (§2.2). Third-party content is not
  covered by the permission (§2.3); the Bank states that the underlying data for its exchange rates is sourced
  from LSEG (formerly Refinitiv). This project uses only the rates the Bank publishes, never underlying data.
- **Request limits (§3):** circumventing limits the Bank imposes on the number or frequency of requests, Valet
  included, is prohibited. No numeric limit appears in the pages read; that does not prove none exists. The guide
  advises: start slow, wait before retrying (increasing back-off), and cache data that changes once a day. This
  project caches every response and never loops on a request.
- **Access key:** none. The guide: "No registration is required to use the Valet API. You do not need an access
  key." The OpenAPI spec (`/valet/static/swagger/api-en.yml`) declares no security scheme.
- **What the rates are:** the Bank calls all its exchange rates **indicative**, released for statistical and
  analytical purposes, not as a benchmark for executing foreign exchange trades. Here they value a fictional book;
  they are not an execution price.
- **Attribution this project uses:** "Exchange rates: Bank of Canada, Valet API
  (https://www.bankofcanada.ca/valet). Indicative rates. Values reproduced unchanged; series and dates selected.
  The Bank of Canada does not endorse this project." It appears in the README, in the book's JSON output, and
  beside any Valet response file committed to the repository.

### Series seen at the first fetch (2026-11-23)

- Group `FX_RATES_DAILY`: 27 series declared (`FX<currency>CAD`, one unit of the currency in Canadian dollars).
  From 2026-11-02 to 2026-11-23, 24 carry a value every day; `FXRUBCAD`, `FXSARCAD` and `FXVNDCAD` carry none.
- 2026-11-11 (Remembrance Day) has no observation: a weekday with no published rate.
- Benchmark bond yields (group `bond_yields_benchmark`: 2, 3, 5, 7, 10 years, long-term, real return bonds) and
  CORRA also exist. The first version does not use them.

## No other source

No other data is downloaded. Trades, positions, counterparties and desk names are invented and marked fictional.
