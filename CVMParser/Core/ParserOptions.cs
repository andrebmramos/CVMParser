namespace FundosParser.Core;

public readonly record struct ParserOptions()
{
    // APENAS TESTE
    public enum TestEnum
    {
        ENUM1,
        ENUM2
    }

    // CONSTANTES
    // Parametrização do arquivo CSV
    public readonly static string HEADER_Cnpj = "CNPJ_FUNDO";
    public readonly static string HEADER_Data = "DT_COMPTC";
    public readonly static string HEADER_Cota = "VL_QUOTA";
    public readonly static string HEADER_NumCotistas = "NR_COTST";
    // Valores max e min para validar anos
    public readonly static int ANO_MIN = 2017; // Arquivos anteriores em histórico compactado
    public readonly static int ANO_MAX = 2100; // A partir de 2100, chamar suporte técnico :-)




    // Range de datas
    public int AnoInicial { get; init; } = 2017;
    public int AnoFinal { get; init; } = DateTime.Today.Year;
    public int MesInicial { get; init; } = 1;
    public int MesFinal { get; init; } = DateTime.Today.Month;

    // Caminhos dos arquivos
    public string PathLeitura { get; init; } = @"c:\temp";  // Pasta dos arquivos baixdos da CVM, padrão c:\temp
    public string PathEscrita { get; init; } = @"c:\temp"; // Pasta para escrita do arquivo filtrado (saída)
    public string FileName { get; init; } = "_DADOS_FILTRADOS";

    // Flags
    public bool EscreverSaida { get; init; } = true;
    public TestEnum testEnum { get; init; } = TestEnum.ENUM1;

    // Nomes curtos, de modo usar, por exemplo
    // comando -in="c:\temp" -out="c:\saida"
    public readonly static Dictionary<string, string> switchMappings = new()
    {
        { "-anoi", nameof(AnoInicial) },
        { "-anof", nameof(AnoFinal) },
        { "-mesi", nameof(MesInicial) },
        { "-mesf", nameof(MesFinal) },

        { "-in", nameof(PathLeitura) },        
        { "-out", nameof(PathEscrita) },
        { "-escrever", nameof(EscreverSaida) },

        { "-nome", nameof(FileName) },

        { "-enum", nameof(testEnum) },
    };

}
