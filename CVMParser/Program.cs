using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;


namespace Program;

// Definição do registro do nosso interesse 
public record Registro(string Cnpj, DateOnly Data, double Cota, int NumCotistas);


public class Program
{

    // Parametrização do arquivo CSV
    readonly static string HEADER_Cnpj        = "CNPJ_FUNDO";
    readonly static string HEADER_Data        = "DT_COMPTC";
    readonly static string HEADER_Cota        = "VL_QUOTA";
    readonly static string HEADER_NumCotistas = "NR_COTST";
    // Valores max e min para validar anos
    readonly static int    ANO_MIN = 2017; // Arquivos anteriores em histórico compactado
    readonly static int    ANO_MAX = 2100; // A partir de 2100, chamar suporte técnico :-)
    // Estruturas principais:
    private static List<Registro> registros = new(); // Lista de registros a serem armazenados
    private static List<string>   buscar = new();    // Lista de CNPJs a serem buscados
    // Caminhos dos arquivos
    private static string pathLeitura = @"c:\temp";  // Pasta dos arquivos baixdos da CVM, padrão c:\temp
    private static string pathEscrita = @"c:\temp";  // Pasta para escrita do arquivo filtrado (saída)


    static void Main(string[] args)
    {
        // Os valores padrão válidos serão mantidos se não forem fornecidos parâmetros
        int anoInicial   = 2021, mesInicial = 1;
        int anoFinal     = 2021, mesFinal = 12;
        bool anosMesesOk = true;

        // Atenção: espera-se SEMPRE receber um arquivo com CNPJs da forma
        // c:\> CVMParser < c:\temp\cnpj.txt
        // onde cnpj.txt = arquivo com um cnpj por linha
        string? cnpj = Console.ReadLine()?.Trim();
        while (!String.IsNullOrEmpty(cnpj))
        {
            Console.WriteLine($"- Solicitado {cnpj}");
            buscar.Add(cnpj);
            cnpj = Console.ReadLine()?.Trim();
        }
        if (buscar.Count < 1)
        {
            Usage();    
            throw new Exception("ERRO: nenhum CNPJ encontrado");
        }


        // Agora, avaliar os args. 
        // 0 argumentos: usar defaults
        // 1 argumento:  tratar como path
        // 4 argumentos: tratar como datas
        // 5 argumentos: datas e path
        Console.WriteLine($"+ Recebidos {args.Count()} argumentos");
        switch (args.Count())
        {
            case 0: break;                 
            case 2: 
                pathLeitura = args[0]; 
                pathEscrita = args[1]; 
                break;
            case 4: 
                ParseAndValidateDatas(); 
                break;
            case 6: 
                ParseAndValidateDatas(); 
                pathLeitura = args[4];
                pathEscrita = args[5];
                break;
            default: Usage(); throw new Exception("ERRO: Uso com 0, 2, 4 ou 6 parâmetros");
        };
        // Resultado da validação já em anosMesesOk
        if (!anosMesesOk)
        {
            Usage();
            throw new Exception("ERRO: Não consegui entender as datas fornecidas");
        }


        // Funcionalidade
        Console.WriteLine($"> Iniciando com Ano-Mes-Inicial: {anoInicial:0000}-{mesInicial:00}");
        Console.WriteLine($">               Ano-Mes-Final:   {anoFinal:0000}-{mesFinal:00}");
        Console.WriteLine($">               Arquivos mensais de dados da CVM lidos de: {pathLeitura}");
        Console.WriteLine($">               Arquivo _DADOS_FILTRADOS.csv escrito em:   {pathLeitura}");
        ParsePeriodo(anoInicial, mesInicial, anoFinal, mesFinal);
        // Console.WriteLine("=== MÉTODO VELHO ===");
        // ParsePeriodoOld(anoInicial, mesInicial, anoFinal, mesFinal);
        // MostrarRegistrosCnpj("30.568.854/0001-30");
        EscreverNovoArquivo(registros);


        // Funções auxiliares internas
        void ParseAndValidateDatas()
        {
            // Valida as conversões
            anosMesesOk = int.TryParse(args[0], out anoInicial) &&
                          int.TryParse(args[1], out mesInicial) &&
                          int.TryParse(args[2], out anoFinal) &&
                          int.TryParse(args[3], out mesFinal);
            // Valida a lógica 
            anosMesesOk &= ValidarAno(anoInicial) && ValidarAno(anoFinal) &&         // Ano entre min e max
                           ValidarMes(mesInicial) && ValidarMes(mesFinal) &&         // Mês entre 1 e 12
                           anoFinal >= anoInicial &&                                 // Ano Final >= inicial
                           (anoInicial != anoFinal ? true : mesFinal >= mesInicial); // Se mesmo ano, exige mêsFinal >= Inicial
        }

        void Usage()
        {
            Console.WriteLine(@"### "); 
            Console.WriteLine(@"### ATENÇÃO PARA AS FORMAS DE USO ###");
            Console.WriteLine(@"### "); 
            Console.WriteLine(@"### 0 argumentos: Fornecer no mínimo arquivo texto com um ");
            Console.WriteLine(@"###         CNPJ por linha. Passar caminho completo da forma");
            Console.WriteLine(@"###         CVMParser < c:\caminho\arquivo_cnpjs.txt");
            Console.WriteLine(@"###         (obs.: a passagem de arquivo não conta como argumento)");
            Console.WriteLine(@"### ");
            Console.WriteLine(@"### 2 argumentos: Esses argumentos serão os caminhos para");
            Console.WriteLine(@"###         leitura dos arquivos da CVM (padrão c:\temp) e ");
            Console.WriteLine(@"###         escrita do arquivos de saída (padrão c:\temp) ");
            Console.WriteLine(@"###         CVMParser c:\downloads c:\filtrados < c:\caminho\arquivo_cnpjs.txt");
            Console.WriteLine(@"### "); 
            Console.WriteLine(@"### 4 argumentos: Serão entendidos como as datas na forma");
            Console.WriteLine(@"###         AnoInicial MesInicial AnoFinal MesFinal");
            Console.WriteLine(@"###         padrão: 2021 01 2021 12;  obs: menor ano 2017");
            Console.WriteLine(@"###         CVMParser 2017 1 2021 12 < c:\caminho\arquivo_cnpjs.txt");
            Console.WriteLine(@"### ");
            Console.WriteLine(@"### 6 argumentos: Serão as datas como acima e o caminho dos>");
            Console.WriteLine(@"###         arquivos da CVM, exemplo");
            Console.WriteLine(@"###         CVMParser 2017 1 2021 12 c:\downloads c:\filtrados < c:\caminho\arquivo_cnpjs.txt");
            Console.WriteLine(@"### ");
        }
        
        bool ValidarAno (int ano) => ano >= ANO_MIN && ano <= ANO_MAX;
        
        bool ValidarMes(int mes) => 1 <= mes && mes <= 12;

    }
   

    // Função de teste
    private static void MostrarRegistrosCnpj(string cnpj)
    {
        Console.WriteLine();
        Console.WriteLine($"REGISTROS DO CNPJ {cnpj}:");
        foreach (var item in registros.Where(r => r.Cnpj.Equals(cnpj)))
            Console.WriteLine(item);
    }


    // Loop por todo período chamando, a cada mês, função que trata respectivo arquivo
    private static void ParsePeriodo(int anoInicial, int mesInicial, int anoFinal, int mesFinal)
    {
        int count = 0;
        // Inicio cronômetro antes do loop
        var watch = new System.Diagnostics.Stopwatch();
        watch.Start();
        for (int ano = anoInicial; ano <= anoFinal; ano++)
            for (int mes = (ano == anoInicial ? mesInicial : 1);
                     mes <= (ano == anoFinal ? mesFinal : 12); mes++)
            {
                ParseAnoMes(ano, mes, buscar, registros);
                count++;
            }
        watch.Stop();
        Console.WriteLine($"> Processados {count} arquivos, tempo: {watch.Elapsed}");
    }

    
    // Tratamentoi do arquivo do respectivo ano e mês
    private static void ParseAnoMes(int ano, int mes, List<string> buscar, List<Registro> registros)
    {
        // Identifico arquivo de dados da CVM do respectivo ano e mês
        string fileName = $@"{pathLeitura}\inf_diario_fi_{ano:0000}{mes:00}.csv";
        Console.Write($"> Processando arquivo {fileName}...");

        // Variáveis auxiliares para tratar quantos CNPJs foram identificados
        int contaAlvosEncontrados = 0;
        string ultimoAlvoEncontrado = "";


        using (var reader = new StreamReader(fileName))
        using (var csv = new CsvReader(reader,
                                       new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" }))
        // InvariantCulture não identifica ";" usado no CSV, enquanto que Cultura BR
        // não identifica "." decimal, portanto altero para invariante MAS
        // especifico o delimitador
        {
            // Leitura do cabeçalho exige esses dois passos
            csv.Read(); csv.ReadHeader();
            // Inicio cronômetro antes do loop
            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();
            while (csv.Read())
            {
                // Leio próximo CNPJ
                string cnpj = csv.GetField(HEADER_Cnpj);
                if (!buscar.Contains(cnpj) && contaAlvosEncontrados < buscar.Count)
                {
                    // Caso não seja buscado, mas ainda não tenha encontrado todos, continue para proxima iteração do loop 
                    // ATENÇÃO: se eu conseguir saber à priori em que ano e mês começam as cotas para os CNPJ em busca, posso evitar
                    // que fique procurando desnecessariamente até o final de arquivos onde o fundo não existe. Para implementar isso,
                    // precisa de um tratamento à parte dos dados.
                    continue;
                } 
                else if (!buscar.Contains(cnpj) && contaAlvosEncontrados >= buscar.Count)
                {
                    // Caso não seja buscado, mas já tenha encontrado todos da lista, quebrar loop (desnecessário continuar percorrendo arquivo)
                    break;
                } 
                else
                {
                    // Se cheguei aqui, estou na linha de um CNPJ buscado.
                    // Só vou incrementar o contador de alvos encontrados se o CNPJ dessa linha
                    // for diferente do último alvo encontrado, ou seja, "encontrei novo alvo"
                    if (cnpj != ultimoAlvoEncontrado)
                    {
                        contaAlvosEncontrados++;  
                        ultimoAlvoEncontrado = cnpj;
                    }
                    // Prossigo lendo demais campos, crio o registro e armazeno na lista de resultados
                    var registro = new Registro
                    (
                        Cnpj: cnpj,
                        Data: csv.GetField<DateOnly>(HEADER_Data),
                        Cota: csv.GetField<double>(HEADER_Cota),
                        NumCotistas: csv.GetField<int>(HEADER_NumCotistas)
                    );
                    registros.Add(registro);
                }              
            }
            watch.Stop();
            Console.WriteLine($"concluído, tempo: {watch.Elapsed}, encontrados {contaAlvosEncontrados} de {buscar.Count} CNPJs");
        }
    }


    // Escreve arquivo de saída
    private static void EscreverNovoArquivo(List<Registro> registros)
    {
        using (var writer = new StreamWriter($@"{pathEscrita}\_DADOS_FILTRADOS.csv"))
        using (var csv = new CsvWriter(writer, CultureInfo.GetCultureInfo("pt-BR")))  // pt-BR para melhor tratamento no Excel
        {
            csv.WriteRecords(registros);
        }
    }



    #region métodos antigos que sempre buscam até fim do arquivo
    private static void ParsePeriodoOld(int anoInicial, int mesInicial, int anoFinal, int mesFinal)
    {
        int count = 0;
        // Inicio cronômetro antes do loop
        var watch = new System.Diagnostics.Stopwatch();
        watch.Start();
        for (int ano = anoInicial; ano <= anoFinal; ano++)
            for (int mes = (ano == anoInicial ? mesInicial : 1);
                     mes <= (ano == anoFinal ? mesFinal : 12); mes++)
            {
                ParseAnoMesOld(ano, mes, buscar, registros);  // Old!
                count++;
            }
        watch.Stop();
        Console.WriteLine($"> Processados {count} arquivos, tempo: {watch.Elapsed}");
    }


    private static void ParseAnoMesOld(int ano, int mes, List<string> buscar, List<Registro> registros)
    {
        // Flexibilizar caminho do diretório base onde estão os arquivos inf_diario....
        string fileName = $@"{pathLeitura}\inf_diario_fi_{ano:0000}{mes:00}.csv";
        Console.Write($"> Processando arquivo {fileName}...");

        using (var reader = new StreamReader(fileName))
        using (var csv = new CsvReader(reader,
                                       new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" }))
        // InvariantCulture não identifica ";" usado no CSV, enquanto que Cultura BR
        // não identifica "." decimal, portanto altero para invariante MAS
        // especifico o delimitador
        {
            // Leitura do cabeçalho exige esses dois passos
            csv.Read(); csv.ReadHeader();
            // Inicio cronômetro antes do loop
            var watch = new System.Diagnostics.Stopwatch();
            watch.Start();
            while (csv.Read())
            {
                // Testar CNPJ. Se não estiver na lista dos buscados, seguir p/próxima iteração com continue
                string cnpj = csv.GetField(HEADER_Cnpj);
                if (!buscar.Contains(cnpj)) continue;

                // Caso seja um CNPJ buscado, cria-se registro e coloca-se na lista
                var registro = new Registro
                (
                    Cnpj: cnpj,
                    Data: csv.GetField<DateOnly>(HEADER_Data),
                    Cota: csv.GetField<double>(HEADER_Cota),
                    NumCotistas: csv.GetField<int>(HEADER_NumCotistas)
                );
                registros.Add(registro);
            }
            watch.Stop();
            Console.WriteLine($"concluído, tempo: {watch.Elapsed}");
        }
    }
    #endregion


}


