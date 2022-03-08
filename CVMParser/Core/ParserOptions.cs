namespace FundosParser.Core;

public readonly record struct ParserOptions()
{

    public enum Comando
    {
        Help,             // Usage
        Parametros,       // Exibe parâmetros
        Cache,            // Cria cache de presenças
        MostraCache,      // Mostra cache de presenças
        Parse, Processar, // PROCESSAMENTO PRINCIPAL, gera arquivo filtrado (objetivo)        
    }

    // CONSTANTES
    // Parametrização do arquivo CSV
    public readonly static string HEADER_Cnpj = "CNPJ_FUNDO";
    public readonly static string HEADER_Data = "DT_COMPTC";
    public readonly static string HEADER_Cota = "VL_QUOTA";
    public readonly static string HEADER_NumCotistas = "NR_COTST";
    // Valores max e min para validar anos
    public readonly static int ANO_MIN = 2005; // Arquivos entre 2005 e 2016 em histórico compactado
    public readonly static int ANO_MAX = 2100; // A partir de 2100, chamar suporte técnico :-)


    // Range de datas
    public int AnoInicial { get; init; } = 2017;
    public int AnoFinal { get; init; } = DateTime.Today.Year;
    public int MesInicial { get; init; } = 1;
    public int MesFinal { get; init; } = DateTime.Today.Month;

    // Caminhos dos arquivos
    public string PathLeitura { get; init; } = @"c:\temp";  // Pasta dos arquivos baixdos da CVM, padrão c:\temp
    public string PathEscrita { get; init; } = @"c:\temp";  // Pasta para escrita do arquivo filtrado (saída)
    public string NomeArquivoFinal { get; init; } = "_DADOS_FILTRADOS";
    public string NomeArquivoCacheDePresencas { get; init; } = ""; // NULO equivale a trabalhar sem cache

    // Flags
    public bool EscreverSaida { get; init; } = true;   // Auxiliar para inibir escrita de arquivo durante desenvolvimento

    // Comando
    public Comando Cmd { get; init; } = Comando.Help;

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

        { "-nome", nameof(NomeArquivoFinal) },
        { "-cache", nameof(NomeArquivoCacheDePresencas) },

        { "-cmd", nameof(Cmd) },

    };

    public bool ValidarPeriodo()
    {
        // Valida a lógica 
        return ValidarAno(AnoInicial) && ValidarAno(AnoFinal) &&         // Ano entre min e max
               ValidarMes(MesInicial) && ValidarMes(MesFinal) &&         // Mês entre 1 e 12
               AnoFinal >= AnoInicial &&                                 // Ano Final >= inicial
               (AnoInicial != AnoFinal ? true : MesFinal >= MesInicial); // Se mesmo ano, exige mêsFinal >= Inicial

        // Funções internas auxiliares
        static bool ValidarAno(int ano) => ano >= ANO_MIN && ano <= ANO_MAX;
        static bool ValidarMes(int mes) => 1 <= mes && mes <= 12;
    }

}
