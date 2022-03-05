using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

using FundosParser.Modelos;

using static FundosParser.Core.ParserOptions;

namespace FundosParser.Core;



public class ParserCore
{
    private readonly ParserOptions _opts;
    private readonly List<Registro> _registros;
    private readonly List<string> _buscar;

    public ParserCore(ParserOptions opts, List<Registro> registros, List<string> buscar)
    {
        _opts=opts;
        _registros=registros;
        _buscar=buscar;
    }


    public void MostrarRegistrosCnpj(string cnpj)
    {
        Console.WriteLine();
        Console.WriteLine($"REGISTROS DO CNPJ {cnpj}:");
        foreach (var item in _registros.Where(r => r.Cnpj.Equals(cnpj)))
            Console.WriteLine(item);
    }


    // Loop por todo período chamando, a cada mês, função que trata respectivo arquivo
    public void ParsePeriodo()
    {
        int anoInicial = _opts.AnoInicial;
        int mesInicial = _opts.MesInicial; 
        int anoFinal   = _opts.AnoFinal;
        int mesFinal   = _opts.MesFinal;
        int count = 0;
        // Inicio cronômetro antes do loop
        var watch = new System.Diagnostics.Stopwatch();
        watch.Start();
        for (int ano = anoInicial; ano <= anoFinal; ano++)
            for (int mes = (ano == anoInicial ? mesInicial : 1);
                     mes <= (ano == anoFinal ? mesFinal : 12); mes++)
            {
                ParseAnoMes(ano, mes, _buscar, _registros);
                count++;
            }
        watch.Stop();
        Console.WriteLine($"> Processados {count} arquivos, tempo: {watch.Elapsed}");
    }


    // Tratamentoi do arquivo do respectivo ano e mês
    private void ParseAnoMes(int ano, int mes, List<string> buscar, List<Registro> registros)
    {
        // Identifico arquivo de dados da CVM do respectivo ano e mês
        string fileName = $@"{_opts.PathLeitura}\inf_diario_fi_{ano:0000}{mes:00}.csv";
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
    public void EscreverNovoArquivo()
    {
        using (var writer = new StreamWriter($@"{_opts.PathEscrita}\{_opts.FileName}.csv"))
        using (var csv = new CsvWriter(writer, CultureInfo.GetCultureInfo("pt-BR")))  // pt-BR para melhor tratamento no Excel
        {
            csv.WriteRecords(_registros);
        }
    }


    public void Usage()
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

    public bool ParseAndValidateDatas()
    {        
        // Valida a lógica 
        return ValidarAno(_opts.AnoInicial) && ValidarAno(_opts.AnoFinal) &&         // Ano entre min e max
               ValidarMes(_opts.MesInicial) && ValidarMes(_opts.MesFinal) &&         // Mês entre 1 e 12
               _opts.AnoFinal >= _opts.AnoInicial &&                                 // Ano Final >= inicial
               (_opts.AnoInicial != _opts.AnoFinal ? true : _opts.MesFinal >= _opts.MesInicial); // Se mesmo ano, exige mêsFinal >= Inicial
    
        // Funções internas auxiliares
        bool ValidarAno(int ano) => ano >= ANO_MIN && ano <= ANO_MAX;
        bool ValidarMes(int mes) => 1 <= mes && mes <= 12;    
    }

    public void MostrarParametros()
    {
        // Funcionalidade
        Console.WriteLine($"> Iniciando com Ano-Mes-Inicial: {_opts.AnoInicial:0000}-{_opts.MesInicial:00}");
        Console.WriteLine($">               Ano-Mes-Final:   {_opts.AnoFinal:0000}-{_opts.MesFinal:00}");
        Console.WriteLine($">               Arquivos mensais de dados da CVM lidos de: {_opts.PathLeitura}");
        Console.WriteLine($">               Arquivo _DADOS_FILTRADOS.csv escrito em:   {_opts.PathLeitura}");
    }


}
