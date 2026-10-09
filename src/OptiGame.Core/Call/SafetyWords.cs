using System.Security.Cryptography;
using System.Text;

namespace OptiGame.Core.Call;

/// <summary>
/// Mots de contrôle de l'appel : 4 mots tirés des empreintes de chiffrement (DTLS) des deux PC. Chacun lit les siens à l'autre (vocal,
/// message ailleurs) : identiques = la connexion est bien directe entre vous ; différents = quelqu'un s'est interposé (code
/// intercepté, serveur de mise en relation compromis) → raccrocher. L'ordre des empreintes ne compte pas : les deux côtés obtiennent les mêmes mots.
/// </summary>
public static class SafetyWords
{
    /// <summary>256 mots courts et distincts (un octet chacun).</summary>
    public static readonly IReadOnlyList<string> Words =
    [
        "abeille", "abricot", "acier", "aigle", "algue", "amande", "ancre", "anneau", "arbre", "argent", "arome", "astre", "atelier", "atome", "aube", "avion",
        "badge", "bague", "baleine", "balle", "banane", "barque", "bassin", "bateau", "berger", "bijou", "biscuit", "bison", "blason", "bocal", "boussole", "branche",
        "brique", "brume", "bureau", "cabane", "cactus", "cadran", "caillou", "calme", "camion", "canard", "canon", "carafe", "carotte", "castor", "cerise", "chalet",
        "chameau", "chapeau", "chaton", "chemin", "chevreuil", "cigale", "citron", "clairon", "clocher", "colline", "comete", "corail", "corbeau", "coton", "crabe", "crayon",
        "cristal", "cygne", "dauphin", "dentelle", "desert", "diamant", "domino", "dragon", "drapeau", "dune", "ecureuil", "elan", "emeraude", "encre", "epice", "etoile",
        "facteur", "falaise", "faucon", "fenetre", "feuille", "figue", "flamme", "fleuve", "foret", "fontaine", "fougere", "fourmi", "fraise", "framboise", "fusee", "galet",
        "gazelle", "girafe", "glacier", "gland", "gomme", "gorille", "grenade", "grenier", "griffon", "grotte", "guitare", "hamac", "hamster", "harpe", "herisson", "hibou",
        "horizon", "huitre", "igloo", "ile", "iris", "ivoire", "jade", "jardin", "jasmin", "jongleur", "jumelle", "jungle", "kayak", "kiwi", "koala", "lagune",
        "lampe", "lanterne", "lapin", "laurier", "lavande", "lezard", "licorne", "lilas", "lion", "loutre", "lune", "lynx", "macaron", "magnolia", "manchot", "mangue",
        "marais", "marmotte", "medaille", "melon", "menthe", "meteore", "miel", "mimosa", "miroir", "moineau", "moulin", "mouette", "muguet", "myrtille", "narval", "navire",
        "neige", "noisette", "nuage", "oasis", "ocean", "olive", "ombre", "opale", "orage", "orange", "orchidee", "ours", "pagode", "palmier", "panda", "papillon",
        "parapluie", "pastel", "pelican", "perle", "phare", "pigeon", "piment", "pinceau", "pingouin", "pirate", "planete", "plume", "poisson", "pomme", "prairie", "puma",
        "quartz", "radeau", "raisin", "rameau", "renard", "requin", "rivage", "robot", "rocher", "roseau", "rubis", "ruisseau", "sable", "safran", "sapin", "saphir",
        "sardine", "satellite", "saule", "sentier", "serpent", "sirene", "soleil", "source", "sphinx", "studio", "sucre", "tambour", "tapis", "tigre", "tomate", "tonnerre",
        "topaze", "tortue", "toucan", "tournesol", "train", "trefle", "tresor", "tulipe", "tunnel", "turquoise", "vague", "vallee", "vanille", "velours", "verger", "violette",
        "volcan", "voilier", "wagon", "yacht", "zebre", "zephyr", "agneau", "ardoise", "biche", "bouleau", "caravane", "cascade", "chouette", "flocon", "grillon", "griotte",
    ];

    /// <summary>4 mots communs aux deux PC ; « » si une empreinte manque (connexion non chiffrée : impossible avec WebRTC).</summary>
    public static string For(string? fingerprintA, string? fingerprintB)
    {
        if (string.IsNullOrWhiteSpace(fingerprintA) || string.IsNullOrWhiteSpace(fingerprintB)) return "";
        var ordered = new[] { Normalize(fingerprintA), Normalize(fingerprintB) }.Order(StringComparer.Ordinal);
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(string.Join("|", ordered)));
        return string.Join(" · ", hash.Take(4).Select(b => Words[b]));
    }

    private static string Normalize(string fingerprint) => fingerprint.Trim().ToUpperInvariant();
}
