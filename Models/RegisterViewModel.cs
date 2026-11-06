using System.ComponentModel.DataAnnotations;

namespace TogetherCraft.Models;

public sealed class RegisterViewModel : IValidatableObject
{
    [Required(ErrorMessage = "Придумай логин.")]
    [RegularExpression("^[A-Za-z0-9_-]{3,24}$", ErrorMessage = "Логин: 3–24 символа, латинские буквы, цифры, дефис или подчёркивание.")]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "Укажи email.")]
    [EmailAddress(ErrorMessage = "Проверь формат email.")]
    [StringLength(254, ErrorMessage = "Email слишком длинный.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Придумай пароль.")]
    [StringLength(128, MinimumLength = 10, ErrorMessage = "Пароль должен содержать не менее 10 символов.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Повтори пароль.")]
    [Compare(nameof(Password), ErrorMessage = "Пароли не совпадают.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = "";

    public bool WantsFabric { get; set; } = true;
    public bool WantsCreate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!WantsFabric && !WantsCreate)
            yield return new ValidationResult("Выбери хотя бы один игровой мир.", new[] { nameof(WantsFabric), nameof(WantsCreate) });
    }
}
