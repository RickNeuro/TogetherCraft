using System.ComponentModel.DataAnnotations;

namespace TogetherCraft.Models;

public sealed class RegisterViewModel
{
    [Required(ErrorMessage = "Придумай логин.")]
    [RegularExpression("^[A-Za-z0-9_-]{3,24}$", ErrorMessage = "Логин: 3–24 символа, латинские буквы, цифры, дефис или подчёркивание.")]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "Укажи Minecraft-ник.")]
    [RegularExpression("^[A-Za-z0-9_]{3,16}$", ErrorMessage = "Minecraft-ник должен содержать 3–16 латинских букв, цифр или подчёркиваний.")]
    public string MinecraftUsername { get; set; } = "";

    [Required(ErrorMessage = "Придумай пароль.")]
    [StringLength(128, MinimumLength = 10, ErrorMessage = "Пароль должен содержать не менее 10 символов.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Повтори пароль.")]
    [Compare(nameof(Password), ErrorMessage = "Пароли не совпадают.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = "";
}
