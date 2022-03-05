using FundosParser.Core;
using FundosParser.Modelos;
using Microsoft.Extensions.Configuration; // Requer: Install-Package Microsoft.Extensions.Hosting



namespace FundosParser;


public class Program
{

    // Estruturas principais:
    private static List<Registro> _registros = new(); // Lista de registros a serem armazenados
    private static List<string>   _buscar = new();    // Lista de CNPJs a serem buscados
    

    static void Main(string[] args)
    {
        // Preparo configurador que lerá os parâmetros de linha de comando
        IConfiguration config = new ConfigurationBuilder()
            .AddCommandLine(args, ParserOptions.switchMappings)
            .Build();

        // Carga dos parâmetros feita pelo configurador
        ParserOptions opts = config.Get<ParserOptions>();


        // Leitura do arquivo de CNPJs
        // Atenção: espera-se SEMPRE receber um arquivo com CNPJs da forma
        // c:\> CVMParser < c:\temp\cnpj.txt
        // onde cnpj.txt = arquivo com um cnpj por linha
        string? cnpj = Console.ReadLine()?.Trim();
        while (!String.IsNullOrEmpty(cnpj))
        {
            Console.WriteLine($"- Solicitado {cnpj}");
            _buscar.Add(cnpj);
            cnpj = Console.ReadLine()?.Trim();
        }
        if (_buscar.Count < 1)
        {            
            throw new Exception("ERRO: nenhum CNPJ encontrado");
        }


        // Criação do Parser
        ParserCore fp = new(opts, _registros, _buscar);
        fp.MostrarParametros();
        fp.ParsePeriodo();
        if (opts.EscreverSaida)
        {
            fp.EscreverNovoArquivo();
        }
        else
        {
            Console.WriteLine("- Ignorando escrita do arquivo de saída");
        }


    }   

}


