using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace FallenSec.Core;

public sealed record FwSpec(string Name, string? App, int Dir, int Proto, string? RemoteAddr, string? RemotePorts)
{
    public string Hash()
    {
        var s = $"{App}|{Dir}|{Proto}|{RemoteAddr}|{RemotePorts}";
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)))[..16];
    }
}

/// <summary>Acces la Windows Firewall (HNetCfg.FwPolicy2 prin COM).</summary>
public sealed class FirewallService : IBlockBackend
{
    public string Name => "Windows Firewall";
    public const string Group = "FallenSec";
    public const int DirIn = 1, DirOut = 2, ProtoAny = 256, ProtoTcp = 6, ProtoUdp = 17;
    readonly object _lock = new();

    static dynamic Policy() => Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;

    /// <summary>nume regulă → descriere (conține hash-ul specificației)</summary>
    public Dictionary<string, string> ListOurRules()
    {
        lock (_lock)
        {
            var d = new Dictionary<string, string>();
            foreach (dynamic r in Policy().Rules)
            {
                string? g = r.Grouping;
                if (g == Group) d[(string)r.Name] = (string?)r.Description ?? "";
            }
            return d;
        }
    }

    public void Add(FwSpec s)
    {
        lock (_lock)
        {
            dynamic r = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule")!)!;
            r.Name = s.Name;
            r.Description = "FallenSec h=" + s.Hash();
            r.Grouping = Group;
            if (!string.IsNullOrEmpty(s.App) && s.App != "System") r.ApplicationName = s.App;
            r.Protocol = s.Proto;
            if (!string.IsNullOrEmpty(s.RemotePorts) && s.Proto != ProtoAny) r.RemotePorts = s.RemotePorts;
            if (!string.IsNullOrEmpty(s.RemoteAddr)) r.RemoteAddresses = s.RemoteAddr;
            r.Direction = s.Dir;
            r.Action = 0; // BLOCK
            r.Profiles = 0x7FFFFFFF;
            r.Enabled = true;
            Policy().Rules.Add(r);
        }
    }

    public void Remove(string name)
    {
        lock (_lock)
        {
            var p = Policy();
            for (int i = 0; i < 20; i++)
            {
                bool found = false;
                foreach (dynamic r in p.Rules) if ((string)r.Name == name && (string?)r.Grouping == Group) { found = true; break; }
                if (!found) break;
                p.Rules.Remove(name);
            }
        }
    }

    public int RemoveAll()
    {
        int n = 0;
        foreach (var name in ListOurRules().Keys) { try { Remove(name); n++; } catch (Exception e) { Log.W("FW remove " + name + ": " + e.Message); } }
        return n;
    }

    /// <summary>Profilurile pe care Windows Firewall e oprit (null = nu pot citi).</summary>
    public List<string>? OffProfiles()
    {
        try
        {
            var p = Policy();
            var off = new List<string>();
            if (!(bool)p.FirewallEnabled[1]) off.Add(L.T("Domeniu", "Domain"));
            if (!(bool)p.FirewallEnabled[2]) off.Add(L.T("Privat", "Private"));
            if (!(bool)p.FirewallEnabled[4]) off.Add("Public");
            return off;
        }
        catch { return null; }
    }

    /// <summary>Firewall-uri terțe active raportate în Windows Security Center (Bitdefender, ESET, Kaspersky...).</summary>
    public static string? ThirdPartyActive()
    {
        try
        {
            dynamic loc = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!)!;
            dynamic svc = loc.ConnectServer(".", @"root\SecurityCenter2");
            var names = new List<string>();
            foreach (dynamic o in svc.ExecQuery("SELECT displayName, productState FROM FirewallProduct"))
            {
                string n = (string)o.displayName;
                long st = Convert.ToInt64(o.productState);
                bool on = ((st >> 12) & 0xF) == 1;
                if (on && !n.Contains("Windows", StringComparison.OrdinalIgnoreCase) && !names.Contains(n)) names.Add(n);
            }
            return names.Count == 0 ? null : string.Join(", ", names);
        }
        catch { return null; }
    }

    /// <summary>true dacă firewall-ul Windows e activ pe toate profilurile.</summary>
    public string? CheckStatus()
    {
        try
        {
            var p = Policy();
            var off = new List<string>();
            if (!(bool)p.FirewallEnabled[1]) off.Add(L.T("Domeniu", "Domain"));
            if (!(bool)p.FirewallEnabled[2]) off.Add(L.T("Privat", "Private"));
            if (!(bool)p.FirewallEnabled[4]) off.Add("Public");
            return off.Count == 0 ? null : L.T("Windows Firewall este oprit pe profilul: ", "Windows Firewall is off for profile: ") + string.Join(", ", off) + L.T(". Blocările nu au efect acolo.", ". Blocks have no effect there.");
        }
        catch (Exception e) { return L.T("Nu pot accesa Windows Firewall: ", "Cannot access Windows Firewall: ") + e.Message; }
    }

    public void EnableAllProfiles()
    {
        lock (_lock) { var p = Policy(); foreach (int pr in new[] { 1, 2, 4 }) p.FirewallEnabled[pr] = true; }
    }

    /// <summary>Toate adresele publice (internet), fără LAN/localhost.</summary>
    public const string InternetRanges =
        "1.0.0.0-9.255.255.255,11.0.0.0-100.63.255.255,100.128.0.0-126.255.255.255,128.0.0.0-169.253.255.255," +
        "169.255.0.0-172.15.255.255,172.32.0.0-192.167.255.255,192.169.0.0-223.255.255.255," +
        "2000::-3fff:ffff:ffff:ffff:ffff:ffff:ffff:ffff";
}
