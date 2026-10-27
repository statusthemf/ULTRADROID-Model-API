private void InspectTextReaders()
{
    var assemblies = AppDomain.CurrentDomain.GetAssemblies();

    foreach (var assembly in assemblies)
    {
        foreach (var typeName in new[]
        {
            "Il2CppSystem.IO.StringReader",
            "Il2CppSystem.IO.TextReader",
            "Il2CppSystem.IO.StreamReader",
            "Il2CppSystem.IO.MemoryStream",
            "Il2CppSystem.IO.Stream"
        })
        {
            var type = assembly.GetType(typeName);
            if (type == null)
                continue;

            Log.LogInfo("--- " + type.FullName + " ---");

            foreach (var ctor in type.GetConstructors(
                BindingFlags.Public | BindingFlags.Instance))
            {
                Log.LogInfo("Constructor: " + ctor);
            }

            foreach (var method in type.GetMethods(
                BindingFlags.Public |
                BindingFlags.Static |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly))
            {
                if (method.Name == "Read" ||
                    method.Name == "ReadToEnd" ||
                    method.Name == "Close" ||
                    method.Name == "Dispose")
                {
                    Log.LogInfo("Method: " + method);
                }
            }
        }
    }
}
