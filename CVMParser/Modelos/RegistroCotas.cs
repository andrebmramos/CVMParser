namespace FundosParser.Modelos;

// Definição do registro do nosso interesse 
public record RegistroCotas(string Cnpj, DateOnly Data, double Cota, int NumCotistas);


