using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

static class ArtifactChecks
{
    // Inspect metadata/IL without loading the plugin or any native runtime assembly.
    internal static void Run(string path, Action<bool, string> check)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var assembly = md.GetAssemblyDefinition();
        check(md.GetString(assembly.Name) == "ChainedBeasts.Matchmaking", "Artifact has the intended plugin assembly identity.");
        var icons = md.ManifestResources.Select(h => md.GetManifestResource(h))
            .Where(r => md.GetString(r.Name).StartsWith("Matchmaking.Icons.", StringComparison.Ordinal)).ToArray();
        var expectedIcons = new[] { "Target", "Heart", "Sorry", "Party", "Lock" };
        check(icons.Length == expectedIcons.Length, "Exactly five CC0 icons are embedded in the shipped plugin.");
        var hashes = new HashSet<string>();
        foreach (var name in expectedIcons)
        {
            var resource = icons.Single(r => md.GetString(r.Name) == "Matchmaking.Icons." + name + ".alpha");
            check(resource.Implementation.IsNil, "Icon " + name + " needs no external artwork file.");
            var reader = pe.GetSectionData(pe.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress)
                .GetReader((int)resource.Offset, 4 + 4096);
            check(reader.ReadInt32() == 4096, "Icon " + name + " has a complete 64x64 alpha mask.");
            var pixels = reader.ReadBytes(4096);
            check(pixels.Count(v => v != 0) is > 100 and < 3000 && pixels.Take(64).All(v => v == 0),
                "Icon " + name + " contains a visible padded silhouette.");
            hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels)));
        }
        check(hashes.Count == expectedIcons.Length, "All five embedded icons are distinct.");
        var references = md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToArray();
        check(!references.Any(n => n.Contains("ModFoundation") || n.Contains("TermsAndContusions") || n.Contains("BattleNumbers")),
            "Ping feature introduces no hard dependency on gameplay mods.");
        var version = ""; var guid = "";
        var controllerCount = 0;
        var nativeWrites = new List<string>();
        var chatLeases = new List<string>();
        var forbiddenFilterCalls = new List<string>();
        var forbiddenLifecycle = new List<string>();
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Static | BindingFlags.Public)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null))
            .ToDictionary(o => unchecked((ushort)o.Value));
        var gameTypes = new HashSet<string> { "GameManager", "NetworkSession", "RunDataManager", "PlayerManager", "Player",
            "PlayerData", "NetworkAgent", "NetworkHealthObject", "AgentModel", "NetworkSceneRoot", "ClientIdentifier", "UIManager", "GameLevel", "GameInput", "PlayerNameFilter", "Crosstales.BWF.BWFManager" };

        foreach (var handle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(handle);
            var name = md.GetString(type.Name);
            if (name == "Plugin")
                foreach (var fieldHandle in type.GetFields())
                {
                    var field = md.GetFieldDefinition(fieldHandle); var constant = field.GetDefaultValue();
                    if (constant.IsNil) continue;
                    var data = md.GetConstant(constant);
                    if (data.TypeCode != ConstantTypeCode.String) continue;
                    var value = Encoding.Unicode.GetString(md.GetBlobBytes(data.Value));
                    if (md.GetString(field.Name) == "Version") version = value;
                    if (md.GetString(field.Name) == "Guid") guid = value;
                }
            if (name is "BrowserController" or "PingController" or "HostToolsController" or "ChatController")
            {
                controllerCount++;
                foreach (var methodHandle in type.GetMethods())
                {
                    var method = md.GetMethodDefinition(methodHandle); var methodName = md.GetString(method.Name);
                    if (methodName is ".ctor" or ".cctor") continue;
                    var hidden = method.GetCustomAttributes().Any(h => AttributeName(md, h).EndsWith(".HideFromIl2CppAttribute", StringComparison.Ordinal));
                    var reader = md.GetBlobReader(method.Signature); var header = reader.ReadSignatureHeader();
                    if (header.IsGeneric) reader.ReadCompressedInteger();
                    var parameters = reader.ReadCompressedInteger(); var result = reader.ReadSignatureTypeCode();
                    var unity = methodName is "Update" or "OnGUI" or "OnDestroy";
                    var scalarProperty = (methodName is "get_IsOpen" or "get_IsWheelOpen" or "get_BlocksInput") && parameters == 0 && result == SignatureTypeCode.Boolean ||
                        (methodName is "set_IsOpen" or "set_IsWheelOpen" or "set_BlocksInput") && parameters == 1 && result == SignatureTypeCode.Void && reader.ReadSignatureTypeCode() == SignatureTypeCode.Boolean;
                    check(hidden || unity && parameters == 0 && result == SignatureTypeCode.Void || scalarProperty,
                        "Injected method has a supported callback surface or is hidden: " + name + "." + methodName);
                }
            }
            if (name is not ("PingController" or "PingNetwork" or "PingWorld" or "GamepadPings" or "ChatNetwork" or "ChatController" or "ChatProfanity")) continue;
            foreach (var methodHandle in type.GetMethods())
            {
                var method = md.GetMethodDefinition(methodHandle);
                if (method.RelativeVirtualAddress == 0) continue;
                var bytes = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
                for (var at = 0; at < bytes.Length;)
                {
                    var first = bytes[at++]; var code = first == 0xfe ? (ushort)(0xfe00 | bytes[at++]) : first;
                    if (!opcodes.TryGetValue(code, out var opcode)) throw new InvalidDataException("Unknown IL opcode.");
                    var size = OperandBytes(opcode.OperandType, bytes, at);
                    if (opcode.OperandType == OperandType.InlineMethod)
                    {
                        var target = MetadataTokens.EntityHandle(BitConverter.ToInt32(bytes, at));
                        if (target.Kind == HandleKind.MethodSpecification) target = md.GetMethodSpecification((MethodSpecificationHandle)target).Method;
                        if (target.Kind == HandleKind.MemberReference)
                        {
                            var member = md.GetMemberReference((MemberReferenceHandle)target);
                            var owner = TypeName(md, member.Parent); var memberName = md.GetString(member.Name);
                            if (name == "ChatProfanity" && (owner.Length == 0 || owner == "PlayerNameFilter" || owner.StartsWith("Crosstales.",StringComparison.Ordinal)) && (memberName is "CreateInstance" or "DeleteInstance" or "Load" or "LoadAll" or "get_Instance" || memberName.StartsWith("set_",StringComparison.Ordinal)))
                                forbiddenFilterCalls.Add(owner + "." + memberName);
                            var chatLease = name == "ChatController" && (owner == "Player" && memberName is "PushInputBlock" or "PopInputBlock" ||
                                owner == "UIManager" && memberName is "PushGameInputBlock" or "PopGameInputBlock");
                            if (chatLease) chatLeases.Add(owner + "." + memberName);
                            if (gameTypes.Contains(owner) && !memberName.StartsWith("get_", StringComparison.Ordinal) && !chatLease && !(name == "ChatProfanity" && owner == "PlayerNameFilter" && memberName == "Sanitize")) nativeWrites.Add(owner + "." + memberName);
                            if (owner == "Unity.Netcode.NetworkManager" && memberName is "DisconnectClient" or "Shutdown" or "StartHost" or "StartClient" or "StartServer")
                                forbiddenLifecycle.Add(owner + "." + memberName);
                        }
                    }
                    at += size;
                }
            }
        }
        check(forbiddenFilterCalls.Count == 0,"Chat filter never creates/configures/loads the native service or uses an auto-creating singleton getter.");
        check(controllerCount == 4, "All four injected controllers were audited from the actual build.");
        check(guid == "local.chainedbeasts.matchmaking", "Existing plugin GUID is preserved.");
        check(assembly.Version.ToString() == version + ".0", "Plugin constant and compiled assembly version agree.");
        check(chatLeases.Count == 4 && chatLeases.Distinct().Count() == 4, "Only chat owns one player/UI push/pop input lease pair in the built plugin.");
        check(nativeWrites.Count == 0, "Game-data calls are getters except the explicit chat input leases: " + string.Join(",", nativeWrites));
        check(forbiddenLifecycle.Count == 0, "Ping/chat classes contain no native start/stop/disconnect calls.");
    }

    private static string AttributeName(MetadataReader md, CustomAttributeHandle handle)
    {
        var attribute = md.GetCustomAttribute(handle);
        return attribute.Constructor.Kind == HandleKind.MemberReference
            ? TypeName(md, md.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent) : "";
    }
    private static string TypeName(MetadataReader md, EntityHandle handle)
    {
        if (handle.Kind == HandleKind.TypeReference)
        {
            var type = md.GetTypeReference((TypeReferenceHandle)handle);
            var ns = md.GetString(type.Namespace); return (ns.Length == 0 ? "" : ns + ".") + md.GetString(type.Name);
        }
        if (handle.Kind == HandleKind.TypeDefinition)
        {
            var type = md.GetTypeDefinition((TypeDefinitionHandle)handle);
            var ns = md.GetString(type.Namespace); return (ns.Length == 0 ? "" : ns + ".") + md.GetString(type.Name);
        }
        return ""; // Generic/native base calls are outside this narrow mutation guard.
    }
    private static int OperandBytes(OperandType type, byte[] bytes, int at) => type switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, at),
        _ => 4
    };
}
