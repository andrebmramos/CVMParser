using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

using FundosParser.Modelos;

using static FundosParser.Core.ParserOptions;

namespace FundosParser.Core;



public class ParserCore
{
        
    // Injetados no ctor
    private readonly ParserOptions _opts;
    private readonly List<string> _buscar;

    // Outros
    private List<RegistroCotas>     _cotas = new();      // informação principal das cotas. Obtenção em Parse
    private List<RegistroPresenca>? _cachePresencas; // cache das datas quando cada fundo de interesse se faz presente

    public ParserCore(ParserOptions opts, List<string> buscar)
    {
        _opts=opts;
        _buscar=buscar;
    }


    // Funções relativas ao cache de presenças
    public void ConstruirCacheDePresencas()
        => ConstruirCacheDePresencas(_opts.AnoInicial, _opts.MesInicial, _opts.AnoFinal, _opts.MesFinal);
    
    private void ConstruirCacheDePresencas(int anoInicial, int mesInicial, int anoFinal, int mesFinal)
    {
        Console.WriteLine($"> Construindo cache de presenças");

        // Resultado
        List<RegistroPresenca> result = new();
        List<string> conhecidos = new();
        
        // Contadores
        int contaDescartesTotal = 0;
        int contaNovosTotal = 0;
        int contaArquivosProcessadosTotal = 0;

        // Cronômetro maior
        var watchTotal = new System.Diagnostics.Stopwatch();
        watchTotal.Start();

        // Loop maior
        for (int ano = anoInicial; ano <= anoFinal; ano++)
        {
            for (int mes = (ano == anoInicial ? mesInicial : 1);
                     mes <= (ano == anoFinal ? mesFinal : 12); mes++)
            {
                // Contadores auxiliares
                int contaDescartes = 0;
                int contaNovos = 0;

                // Identifico arquivo CSV
                string fileName = $@"{_opts.PathLeitura}\inf_diario_fi_{ano:0000}{mes:00}.csv";
                Console.Write($"> Buscando CNPJs em {fileName}...");

                // Crio recursos
                using (var reader = new StreamReader(fileName))
                using (var csv = new CsvReader(reader,
                                               new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" }))
                {
                    // Leitura do cabeçalho exige esses dois passos
                    csv.Read(); csv.ReadHeader();
                    // Cronômetro menor (leitura de único arquivo)
                    var watcArquivo = new System.Diagnostics.Stopwatch();
                    watcArquivo.Start();
                    // Loop do arquivo
                    string cnpj_anterior = "";
                    while (csv.Read())
                    {
                        string cnpj = csv.GetField(HEADER_Cnpj);
                        if (cnpj != cnpj_anterior && _buscar.Contains(cnpj))
                        {
                            // nesse caso, achei linha com novo cnpj. Atualizo anterior e processo:
                            cnpj_anterior = cnpj;
                            if (conhecidos.Contains(cnpj))  // obs.: trabalhar direto nba lista de resultados é muito menos eficiente: (result.Exists(r => r.Cnpj==cnpj))
                            {
                                // CNPJ já estava identificado: pulo linhas para ganhar tempo e continuo
                                contaDescartes++;
                                continue;
                            }
                            else 
                            {
                                // CNPJ não estava identificado: crio novo registro da sua presença a partir deste mês
                                contaNovos++;
                                conhecidos.Add(cnpj);
                                var rp = new RegistroPresenca(cnpj, ano, mes);
                                result.Add(new RegistroPresenca(cnpj, ano, mes));
                            }   
                        }
                        else
                        {
                            contaDescartes++;
                        }

                    }
                    watcArquivo.Stop();
                    Console.WriteLine($"concluído; Tempo: {watcArquivo.Elapsed}, Encontrados {contaNovos}, Descartados {contaDescartes}");
                    contaDescartesTotal += contaDescartes;
                    contaNovosTotal += contaNovos;
                    contaArquivosProcessadosTotal++;
                }
            }
        }
        watchTotal.Stop();
        _cachePresencas = result;
        Console.WriteLine($"> Concluída busca em {contaArquivosProcessadosTotal} arquivos; Tempo: {watchTotal.Elapsed}, Encontrados {contaNovosTotal}; Descartadas {contaDescartesTotal} leituras");
        SalvarCacheDePresencas();
    }

    public void MostrarCacheDePresencas()
    {
        Console.WriteLine("> Registro de Presenças:");
        foreach (var item in _cachePresencas)
        {
            Console.WriteLine($"> {item}");
        }
    }

    private void SalvarCacheDePresencas()
    {
        Console.WriteLine("> Salvando arquivo de presenças:");
        using (var writer = new StreamWriter($@"{_opts.PathLeitura}\{_opts.NomeArquivoCacheDePresencas}.csv"))
        using (var csv = new CsvWriter(writer, CultureInfo.GetCultureInfo("pt-BR")))  // pt-BR para melhor tratamento no Excel
        {
            csv.WriteRecords(_cachePresencas);
        }
    }

    private void LerCacheDePresencasDeArquivo()
    {       
        using (var reader = new StreamReader($@"{_opts.PathLeitura}\{_opts.NomeArquivoCacheDePresencas}.csv"))
        using (var csv = new CsvReader(reader,
                                       new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" }))
            _cachePresencas = csv.GetRecords<RegistroPresenca>().ToList();
    }


    // Funções principais  
    public void ParsePeriodo()
    {
        // Verificar se datas estão válidas
        if (!ValidarPeriodo())
        {
            throw new ArgumentException("Datas fornecidas inválidas");
        }
        // Utilizara cache se foi informado arquivo, senão desprezará
        bool utilizarCacheDePresencas = _opts.NomeArquivoCacheDePresencas != "";
        // Inicio cronômetro antes do loop
        var watch = new System.Diagnostics.Stopwatch();
        watch.Start();
        // Loop principal
        int mesesProcessados = 0;
        for (int ano = _opts.AnoInicial; ano <= _opts.AnoFinal; ano++)
        {
            for (int mes = (ano == _opts.AnoInicial ? _opts.MesInicial : 1);
                     mes <= (ano == _opts.AnoFinal ? _opts.MesFinal : 12); mes++)
            {
                if (utilizarCacheDePresencas)
                {
                    ParseAnoMesComCache(ano, mes, _buscar, _cotas);
                }
                else 
                {
                    ParseAnoMes(ano, mes, _buscar, _cotas);
                }
                mesesProcessados++;
            }
        }
        // Fim
        watch.Stop();
        Console.WriteLine($"> Processados {mesesProcessados} arquivos, tempo: {watch.Elapsed}");
        EscreverNovoArquivo();

        // Auxiliares
        bool ValidarPeriodo()
        {
            // Valida a lógica 
            return ValidarAno(_opts.AnoInicial) && ValidarAno(_opts.AnoFinal) &&         // Ano entre min e max
                   ValidarMes(_opts.MesInicial) && ValidarMes(_opts.MesFinal) &&         // Mês entre 1 e 12
                   _opts.AnoFinal >= _opts.AnoInicial &&                                 // Ano Final >= inicial
                   (_opts.AnoInicial != _opts.AnoFinal ? true : _opts.MesFinal >= _opts.MesInicial); // Se mesmo ano, exige mêsFinal >= Inicial

            // Funções internas auxiliares
        }
        static bool ValidarAno(int ano) => ano >= ANO_MIN && ano <= ANO_MAX;
        static bool ValidarMes(int mes) => 1 <= mes && mes <= 12;
    }
    
    private void ParseAnoMes(int ano, int mes, List<string> buscar, List<RegistroCotas> registros)
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
                    var registro = new RegistroCotas
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

    private void ParseAnoMesComCache(int ano, int mes, List<string> buscar, List<RegistroCotas> registros)
    {
        // Garantir que cache de presenças esteja carregado
        if (_cachePresencas==null) LerCacheDePresencasDeArquivo();
            
        // Ideia: criar uma nova lista de cnpjs a buscar que contenha apenas os CNPJs
        // presentes neste mês e passá-la como argumento apra a função ParseAnoMes
        List<string> presentes = new();

        // Confrontar cada CNPJ da lista de busca com o cache de presenças
        foreach (var cnpj in _buscar)
        {
            // Procuro cnpj no cache de presenças
            RegistroPresenca? rp = _cachePresencas?.SingleOrDefault(x => x.Cnpj == cnpj);
            if (rp == null)
            {
                // cnpj não localizado no cache: abortar 
                throw new Exception($"CNPJ {cnpj} não localizado no cache. Reconstruir cache ou executar sem.");
            }
            else
            {
                // cnpj localizado no cache. Incluir na lista de "presentes em cache"
                if (ConfirmarPresencaNoPeriodo(rp, ano, mes))
                {
                    presentes.Add(cnpj);
                }
            }
        }

        // Conclusão: prosseguir com o parse apenas dos CNPJs que sei que constarão no ano e mês em questão
        Console.Write($"> [Cache ativo]: ");
        ParseAnoMes(ano, mes, presentes, registros);        

        // Auxiliar
        static bool ConfirmarPresencaNoPeriodo(RegistroPresenca rp, int ano, int mes) => rp switch
        {
            RegistroPresenca r when r.Ano < ano => true,           // cnpj presente já em ano anterior ao pesquisado
            RegistroPresenca r when r.Ano > ano => false,          // cnpj presente somente em ano posterior ao pesquisado
            RegistroPresenca r when r.Ano == ano => r.Mes <= mes,  // pesquisando cnpj em ano registrado, verificar mês
            _ => throw new Exception($"Erro em {nameof(ConfirmarPresencaNoPeriodo)}")
        };
    }

    private void EscreverNovoArquivo()
    {
        if (_opts.EscreverSaida)
        {
            using (var writer = new StreamWriter($@"{_opts.PathEscrita}\{_opts.NomeArquivoFinal}.csv"))
            using (var csv = new CsvWriter(writer, CultureInfo.GetCultureInfo("pt-BR")))  // pt-BR para melhor tratamento no Excel
            {
                csv.WriteRecords(_cotas);
            }            
        }
        else
        {
            Console.WriteLine("- Ignorando escrita do arquivo de saída");
        }
    }

    
    // Funções auxiliares
    public static void Usage()
    {
        Console.WriteLine(@"### ");
        Console.WriteLine(@"### ATENÇÃO PARA A FORMA DE USO ###");
        Console.WriteLine(@"### ");
        Console.WriteLine(@"### FundosParser -op1=OPCAO1 -op2=OPCAO2 ... < c:\caminho\arquivo_cnpjs.txt");
        Console.WriteLine(@"### ");
        Console.WriteLine(@"### Ver as opções no arquivo ParserOptions.cs");
        Console.WriteLine(@"### ");
    }    

    public void MostrarParametrosResumidos()
    {
        // Funcionalidade
        Console.WriteLine($"> Iniciando com Ano-Mes-Inicial:  {_opts.AnoInicial:0000}-{_opts.MesInicial:00}");
        Console.WriteLine($">               Ano-Mes-Final:    {_opts.AnoFinal:0000}-{_opts.MesFinal:00}");
        Console.WriteLine($">               Pasta de leitura: {_opts.PathLeitura}");
        Console.WriteLine($">               Pasta de escrita: {_opts.PathEscrita}");
        Console.WriteLine($">               Arquivo de saída: {_opts.NomeArquivoFinal}.csv");
        Console.WriteLine($">               Arquivo de cache: {_opts.NomeArquivoCacheDePresencas}.csv");
    }

    public void MostrarParametros()
    {
        // Funcionalidade
        Console.WriteLine($"> Parâmetros:");
        Console.WriteLine($"> {_opts}");
    }

    public void MostrarRegistrosCnpj(string cnpj)
    {
        Console.WriteLine();
        Console.WriteLine($"REGISTROS DO CNPJ {cnpj}:");
        foreach (var item in _cotas.Where(r => r.Cnpj.Equals(cnpj)))
            Console.WriteLine(item);
    }

}
