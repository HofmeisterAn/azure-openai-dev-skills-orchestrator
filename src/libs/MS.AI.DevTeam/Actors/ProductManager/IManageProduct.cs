namespace MS.AI.DevTeam;

public interface IManageProduct : IGrainWithIntegerCompoundKey, IChatHistory
{
    Task<string> CreateReadme(string ask);
}