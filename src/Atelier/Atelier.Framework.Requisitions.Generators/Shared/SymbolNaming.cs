namespace Atelier.Framework.Generators.Requisition;

public static class SymbolNaming
{
    public static string ImplName(string interfaceName)
    {
        if (interfaceName.Length >= 2
            && interfaceName[0] == 'I'
            && char.IsUpper(interfaceName[1]))
        {
            return interfaceName.Substring(1);
        }

        return interfaceName;
    }
}
